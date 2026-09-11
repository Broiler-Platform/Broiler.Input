using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Windows;

namespace Broiler.Input.Camera.Windows;

internal sealed class WindowsCameraCaptureSession : IDisposable, IAsyncDisposable
{
    private const int MediaFoundationTimeFrequency = 10_000_000;
    private const int VideoStreamIndex = WindowsMediaFoundationNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM;

    private readonly Lock _gate = new();
    private readonly InputDeviceDescriptor _descriptor;
    private readonly CameraOpenOptions _options;
    private readonly Action<InputFault> _faulted;
    private readonly Action<CameraFormat> _formatNegotiated;
    private readonly Action<CameraCaptureStatistics> _statisticsChanged;
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly WindowsCaptureWorker<CameraFrameLease> _worker;
    private readonly Lock _statisticsGate = new();

    private IMFSourceReader? _sourceReader;
    private long _capturedCount;
    private long _formatChangedCount;
    private long _discontinuousCount;
    private long _frameNumber;

    public WindowsCameraCaptureSession(InputDeviceDescriptor descriptor, CameraOpenOptions options,
        Action<CameraFrameLease> deliver, Action<InputFault> faulted, Action<CameraFormat> formatNegotiated,
        Action<CameraCaptureStatistics> statisticsChanged, IInputClock clock, IInputDiagnosticSink? diagnostics)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentNullException.ThrowIfNull(deliver);
        _faulted = faulted ?? throw new ArgumentNullException(nameof(faulted));
        _formatNegotiated = formatNegotiated ?? throw new ArgumentNullException(nameof(formatNegotiated));
        _statisticsChanged = statisticsChanged ?? throw new ArgumentNullException(nameof(statisticsChanged));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        _worker = new WindowsCaptureWorker<CameraFrameLease>("Broiler.Input.Camera.Windows.MediaFoundation",
            _options.SessionOptions.DeliveryOptions, RunCapture, InterruptCapture, deliver, TranslateFailure,
            exception => _faulted(((InputCameraException)exception).Fault), ReportCallbackFailure, PublishStatistics);
    }

    public Task StartAsync(CancellationToken cancellationToken) => _worker.StartAsync(cancellationToken);

    public void EnableDelivery() => _worker.EnableDelivery();

    public ValueTask StopAsync(CancellationToken cancellationToken) => _worker.StopAsync(cancellationToken);

    public void Dispose() => _worker.Dispose();

    public ValueTask DisposeAsync() => _worker.DisposeAsync();

    private void InterruptCapture()
    {
        // The worker runs interruption on a pool thread. Hold the native-lifetime
        // gate so cleanup cannot release the source reader while it is flushed.
        lock (_gate)
        {
            if (_sourceReader is not { } reader)
                return;

            bool uninitialize = false;
            try
            {
                int result = WindowsMediaFoundationNative.CoInitializeEx(IntPtr.Zero, WindowsMediaFoundationNative.COINIT_MULTITHREADED);
                uninitialize = result is WindowsMediaFoundationNative.S_OK or WindowsMediaFoundationNative.S_FALSE;
                WindowsCameraFaults.ThrowIfFailed(reader.Flush(VideoStreamIndex), "Camera source reader flush failed.");
            }
            catch (Exception exception)
            {
                ReportCallbackFailure(exception);
            }
            finally
            {
                if (uninitialize)
                    WindowsMediaFoundationNative.CoUninitialize();
            }
        }
    }

    private void RunCapture()
    {
        Interlocked.Exchange(ref _capturedCount, 0);
        Interlocked.Exchange(ref _formatChangedCount, 0);
        Interlocked.Exchange(ref _discontinuousCount, 0);
        Interlocked.Exchange(ref _frameNumber, 0);

        object? activateObject = null;
        object? mediaSourceObject = null;
        object? sourceReaderAttributesObject = null;
        object? sourceReaderObject = null;
        IMFMediaSource? mediaSource = null;
        MediaFoundationPlatformScope? platform = null;

        try
        {
            platform = new MediaFoundationPlatformScope();
            mediaSource = WindowsCameraDeviceEnumerator.ActivateMediaSource(_descriptor, out activateObject, out mediaSourceObject);
            IMFAttributes sourceReaderAttributes = CreateSourceReaderAttributes();
            sourceReaderAttributesObject = sourceReaderAttributes;

            WindowsCameraFaults.ThrowIfFailed(
                WindowsMediaFoundationNative.MFCreateSourceReaderFromMediaSource(mediaSource, sourceReaderAttributes, out IMFSourceReader reader),
                "Media Foundation camera source reader creation failed.");

            sourceReaderObject = reader;

            lock (_gate)
                _sourceReader = reader;

            WindowsCameraFaults.ThrowIfFailed(
                reader.SetStreamSelection(VideoStreamIndex, true),
                "Media Foundation camera stream selection failed.");

            CameraFormat negotiatedFormat = NegotiateFormat(reader);
            _formatNegotiated(negotiatedFormat);
            _worker.SignalStarted();

            while (!_worker.IsStopRequested)
                ReadOneFrame(reader, ref negotiatedFormat);
        }
        finally
        {
            lock (_gate)
                _sourceReader = null;

            mediaSource?.Shutdown();
            ReleaseComObject(sourceReaderObject);
            ReleaseComObject(sourceReaderAttributesObject);
            ReleaseComObject(mediaSourceObject);

            if (activateObject is IMFActivate activate)
                activate.ShutdownObject();

            ReleaseComObject(activateObject);
            platform?.Dispose();
        }
    }

    private static IMFAttributes CreateSourceReaderAttributes()
    {
        WindowsCameraFaults.ThrowIfFailed(
            WindowsMediaFoundationNative.MFCreateAttributes(out IMFAttributes attributes, 2),
            "Media Foundation camera source reader attribute store creation failed.");

        try
        {
            Guid advancedVideoProcessing = WindowsMediaFoundationNative.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING;
            WindowsCameraFaults.ThrowIfFailed(
                attributes.SetUINT32(ref advancedVideoProcessing, 1),
                "Media Foundation camera source reader video processing configuration failed.");

            Guid disconnectOnShutdown = WindowsMediaFoundationNative.MF_SOURCE_READER_DISCONNECT_MEDIASOURCE_ON_SHUTDOWN;
            WindowsCameraFaults.ThrowIfFailed(
                attributes.SetUINT32(ref disconnectOnShutdown, 1),
                "Media Foundation camera source reader shutdown configuration failed.");

            return attributes;
        }
        catch
        {
            ReleaseComObject(attributes);
            throw;
        }
    }

    private CameraFormat NegotiateFormat(IMFSourceReader reader)
    {
        CameraFormat? preferred = _options.PreferredFormat;

        if (preferred is null)
            return NegotiateDefaultFormat(reader);

        for (int index = 0; ; index++)
        {
            int result = reader.GetNativeMediaType(VideoStreamIndex, index, out IMFMediaType nativeType);
            if (result == WindowsMediaFoundationNative.MF_E_NO_MORE_TYPES)
                break;

            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera media type enumeration failed.");

            try
            {
                CameraFormat format = WindowsCameraMediaType.ReadFormat(nativeType);
                if (!FormatMatches(preferred, format))
                    continue;

                result = reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType);
                WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera media type selection failed.");

                return format;
            }
            finally
            {
                ReleaseComObject(nativeType);
            }
        }

        throw WindowsCameraFaults.CreateException(WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE,
            $"Requested camera format {preferred.Width}x{preferred.Height} {preferred.PixelFormat} {preferred.FrameRateNumerator}/{preferred.FrameRateDenominator} is not available.");
    }

    private static CameraFormat NegotiateDefaultFormat(IMFSourceReader reader)
    {
        int fallbackIndex = -1;

        for (int index = 0; ; index++)
        {
            int result = reader.GetNativeMediaType(VideoStreamIndex, index, out IMFMediaType nativeType);
            if (result == WindowsMediaFoundationNative.MF_E_NO_MORE_TYPES)
                break;

            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera media type enumeration failed.");

            try
            {
                CameraFormat format = WindowsCameraMediaType.ReadFormat(nativeType);

                if (fallbackIndex < 0)
                    fallbackIndex = index;

                if (!IsPreferredDefaultFormat(format))
                    continue;

                result = reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType);
                WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera default media type selection failed.");

                return format;
            }
            finally
            {
                ReleaseComObject(nativeType);
            }
        }

        if (fallbackIndex >= 0)
            return SelectNativeMediaType(reader, fallbackIndex);

        int currentResult = reader.GetCurrentMediaType(VideoStreamIndex, out IMFMediaType currentType);

        if (currentResult >= 0)
        {
            try
            {
                return WindowsCameraMediaType.ReadFormat(currentType);
            }
            finally
            {
                ReleaseComObject(currentType);
            }
        }

        WindowsCameraFaults.ThrowIfFailed(currentResult, "Media Foundation camera current media type lookup failed.");
        throw WindowsCameraFaults.CreateException(WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE, "Camera source did not expose a readable media type.");
    }

    private static CameraFormat SelectNativeMediaType(IMFSourceReader reader, int index)
    {
        int result = reader.GetNativeMediaType(VideoStreamIndex, index, out IMFMediaType nativeType);
        WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera default media type lookup failed.");

        try
        {
            result = reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType);
            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera default media type selection failed.");

            return WindowsCameraMediaType.ReadFormat(nativeType);
        }
        finally
        {
            ReleaseComObject(nativeType);
        }
    }

    private static bool IsPreferredDefaultFormat(CameraFormat format) =>
        format.PixelFormat is CameraPixelFormat.Bgra32 or
            CameraPixelFormat.Rgba32 or
            CameraPixelFormat.Rgb24 or
            CameraPixelFormat.Nv12 or
            CameraPixelFormat.Yuy2 or
            CameraPixelFormat.Gray8;

    private void ReadOneFrame(IMFSourceReader reader, ref CameraFormat negotiatedFormat)
    {
        int result = reader.ReadSample(VideoStreamIndex, 0, out _, out SourceReaderFlags streamFlags,
            out long timestamp, out IMFSample? sample);

        if (result == WindowsMediaFoundationNative.MF_E_SHUTDOWN)
            throw WindowsCameraFaults.CreateException(result, "The camera source was shut down.");

        WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera sample read failed.");

        CameraFrameFlags frameFlags = CameraFrameFlags.None;

        if ((streamFlags & SourceReaderFlags.Error) != 0)
            throw WindowsCameraFaults.CreateException(WindowsMediaFoundationNative.MF_E_SHUTDOWN, "The camera source reader reported an error.");

        if ((streamFlags & SourceReaderFlags.EndOfStream) != 0)
            frameFlags |= CameraFrameFlags.EndOfStream;

        if ((streamFlags & SourceReaderFlags.StreamTick) != 0)
            frameFlags |= CameraFrameFlags.Discontinuous;

        if ((streamFlags & SourceReaderFlags.CurrentMediaTypeChanged) != 0 ||
            (streamFlags & SourceReaderFlags.NativeMediaTypeChanged) != 0)
        {
            if (_options.SessionOptions.ReportFormatChanges)
                frameFlags |= CameraFrameFlags.FormatChanged;

            Interlocked.Increment(ref _formatChangedCount);

            if (reader.GetCurrentMediaType(VideoStreamIndex, out IMFMediaType changedType) >= 0)
            {
                try
                {
                    negotiatedFormat = WindowsCameraMediaType.ReadFormat(changedType);
                    _formatNegotiated(negotiatedFormat);
                }
                finally
                {
                    ReleaseComObject(changedType);
                }
            }
        }

        if ((frameFlags & CameraFrameFlags.Discontinuous) != 0)
            Interlocked.Increment(ref _discontinuousCount);

        if (sample is null)
        {
            PublishStatistics();
            return;
        }

        try
        {
            CaptureSample(sample, negotiatedFormat, timestamp, frameFlags);
        }
        finally
        {
            ReleaseComObject(sample);
        }
    }

    private void CaptureSample(IMFSample sample, CameraFormat format, long timestamp, CameraFrameFlags flags)
    {
        int result = sample.GetBufferCount(out int bufferCount);
        WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera sample buffer count lookup failed.");

        if (bufferCount <= 0)
        {
            PublishStatistics();
            return;
        }

        result = sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera buffer conversion failed.");

        try
        {
            result = buffer.Lock(out IntPtr data, out _, out int currentLength);
            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera buffer lock failed.");

            byte[] bytes = new byte[currentLength];

            try
            {
                if (currentLength > 0 && data != IntPtr.Zero)
                    Marshal.Copy(data, bytes, 0, currentLength);
            }
            finally
            {
                buffer.Unlock();
            }

            InputTimestamp frameTimestamp = timestamp >= 0
                ? new InputTimestamp(timestamp, MediaFoundationTimeFrequency, "Windows.MediaFoundation.PresentationTime")
                : _clock.GetTimestamp();

            if (timestamp < 0)
                flags |= CameraFrameFlags.TimestampError;

            CameraFrameLease lease = new(bytes, format, WindowsCameraMediaType.CreatePlanes(format, currentLength), frameTimestamp, _frameNumber++, flags);

            Interlocked.Increment(ref _capturedCount);

            _worker.TryEnqueue(lease);

            PublishStatistics();
        }
        finally
        {
            ReleaseComObject(buffer);
        }
    }

    private void PublishStatistics()
    {
        lock (_statisticsGate)
        {
            InputDeliveryMetrics metrics = _worker.Metrics;
            _statisticsChanged(new CameraCaptureStatistics(Interlocked.Read(ref _capturedCount), metrics.DequeuedCount,
                metrics.DroppedNewestCount, metrics.DroppedOldestCount, Interlocked.Read(ref _formatChangedCount), Interlocked.Read(ref _discontinuousCount), metrics.QueueDepth));
        }
    }

    private static Exception TranslateFailure(Exception exception) => exception is InputCameraException
        ? exception
        : new InputCameraException(new InputFault(InputErrorCategory.NativeFailure,
            "MediaFoundation camera capture failed.", exception, nativeFacility: "MediaFoundation"));

    private void ReportCallbackFailure(Exception exception) =>
        _diagnostics.Write(new InputDiagnosticEvent(InputDiagnosticLevel.Error, "camera.callback.failed",
            _clock.GetTimestamp(), _descriptor.Id, InputErrorCategory.NativeFailure,
            new Dictionary<string, string>
            {
                ["exception"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["message"] = exception.Message,
            }));

    private static bool FormatMatches(CameraFormat expected, CameraFormat actual) =>
        expected.Width == actual.Width &&
        expected.Height == actual.Height &&
        expected.FrameRateNumerator == actual.FrameRateNumerator &&
        expected.FrameRateDenominator == actual.FrameRateDenominator &&
        expected.PixelFormat == actual.PixelFormat;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
