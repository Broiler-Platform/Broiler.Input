using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input;
using Broiler.Input.Camera;

namespace Broiler.Input.Camera.Windows;

internal sealed class WindowsCameraCaptureSession : IDisposable, IAsyncDisposable
{
    private const int MediaFoundationTimeFrequency = 10_000_000;
    private const int VideoStreamIndex = WindowsMediaFoundationNative.MF_SOURCE_READER_FIRST_VIDEO_STREAM;

    private readonly object _gate = new();
    private readonly InputDeviceDescriptor _descriptor;
    private readonly CameraOpenOptions _options;
    private readonly Action<CameraFrameLease> _deliver;
    private readonly Action<InputFault> _invalidated;
    private readonly Action<CameraFormat> _formatNegotiated;
    private readonly Action<CameraCaptureStatistics> _statisticsChanged;
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly WindowsCameraDeliveryQueue _deliveryQueue;

    private Thread? _thread;
    private TaskCompletionSource<object?>? _started;
    private IMFSourceReader? _sourceReader;
    private bool _stopRequested;
    private bool _callbacksEnabled;
    private bool _disposed;
    private long _capturedCount;
    private long _deliveredCount;
    private long _formatChangedCount;
    private long _discontinuousCount;
    private long _frameNumber;

    public WindowsCameraCaptureSession(
        InputDeviceDescriptor descriptor,
        CameraOpenOptions options,
        Action<CameraFrameLease> deliver,
        Action<InputFault> invalidated,
        Action<CameraFormat> formatNegotiated,
        Action<CameraCaptureStatistics> statisticsChanged,
        IInputClock clock,
        IInputDiagnosticSink? diagnostics)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        _invalidated = invalidated ?? throw new ArgumentNullException(nameof(invalidated));
        _formatNegotiated = formatNegotiated ?? throw new ArgumentNullException(nameof(formatNegotiated));
        _statisticsChanged = statisticsChanged ?? throw new ArgumentNullException(nameof(statisticsChanged));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        _deliveryQueue = new WindowsCameraDeliveryQueue(_options.SessionOptions.DeliveryOptions);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        TaskCompletionSource<object?> started;
        lock (_gate)
        {
            if (_thread is { IsAlive: true })
                return;

            _stopRequested = false;
            _callbacksEnabled = true;
            _capturedCount = 0;
            _deliveredCount = 0;
            _formatChangedCount = 0;
            _discontinuousCount = 0;
            _frameNumber = 0;
            _started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            started = _started;
            _thread = new Thread(RunCapture)
            {
                IsBackground = true,
                Name = "Broiler.Input.Camera.Windows.MediaFoundation",
            };
            _thread.Start();
        }

        try
        {
            await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        Thread? thread;
        IMFSourceReader? reader;
        lock (_gate)
        {
            _callbacksEnabled = false;
            _stopRequested = true;
            thread = _thread;
            reader = _sourceReader;
        }

        if (reader is not null)
            FlushSourceReaderForStop(reader);

        while (thread is { IsAlive: true })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (thread.Join(25))
                break;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_thread, thread))
                _thread = null;
            _deliveryQueue.DisposeAll();
            PublishStatistics();
        }

        return ValueTask.CompletedTask;
    }

    private void FlushSourceReaderForStop(IMFSourceReader reader)
    {
        Exception? failure = null;
        var flushThread = new Thread(() =>
        {
            bool shouldUninitializeCom = false;
            try
            {
                int comResult = WindowsMediaFoundationNative.CoInitializeEx(IntPtr.Zero, WindowsMediaFoundationNative.COINIT_MULTITHREADED);
                shouldUninitializeCom = comResult == WindowsMediaFoundationNative.S_OK || comResult == WindowsMediaFoundationNative.S_FALSE;
                reader.Flush(VideoStreamIndex);
            }
            catch (Exception exception) when (exception is InvalidCastException or COMException)
            {
                failure = exception;
            }
            finally
            {
                if (shouldUninitializeCom)
                    WindowsMediaFoundationNative.CoUninitialize();
            }
        })
        {
            IsBackground = true,
            Name = "Broiler.Input.Camera.Windows.SourceReaderFlush",
        };

        flushThread.SetApartmentState(ApartmentState.MTA);
        flushThread.Start();
        flushThread.Join();

        if (failure is not null)
        {
            _diagnostics.Write(new InputDiagnosticEvent(
                InputDiagnosticLevel.Warning,
                "camera.source_reader.flush_failed",
                _clock.GetTimestamp(),
                _descriptor.Id,
                InputErrorCategory.NativeFailure,
                new Dictionary<string, string>
                {
                    ["exception"] = failure.GetType().FullName ?? failure.GetType().Name,
                    ["message"] = failure.Message,
                }));
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
    }

    private void RunCapture()
    {
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
            _started?.TrySetResult(null);

            while (!IsStopRequested())
                ReadOneFrame(reader, ref negotiatedFormat);
        }
        catch (InputCameraException exception)
        {
            _started?.TrySetException(exception);
            if (exception.Fault.Category == InputErrorCategory.DeviceRemoved && AreCallbacksEnabled())
                _invalidated(exception.Fault);
        }
        catch (Exception exception)
        {
            InputFault fault = new(
                InputErrorCategory.NativeFailure,
                "Media Foundation camera capture failed.",
                exception,
                nativeFacility: "MediaFoundation");
            _started?.TrySetException(new InputCameraException(fault));
        }
        finally
        {
            lock (_gate)
                _sourceReader = null;

            _deliveryQueue.DisposeAll();
            PublishStatistics();
            if (mediaSource is not null)
                mediaSource.Shutdown();
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
            int result = reader.GetNativeMediaType(
                VideoStreamIndex,
                index,
                out IMFMediaType nativeType);
            if (result == WindowsMediaFoundationNative.MF_E_NO_MORE_TYPES)
                break;
            WindowsCameraFaults.ThrowIfFailed(result, "Media Foundation camera media type enumeration failed.");

            try
            {
                CameraFormat format = WindowsCameraMediaType.ReadFormat(nativeType);
                if (!FormatMatches(preferred, format))
                    continue;

                WindowsCameraFaults.ThrowIfFailed(
                    reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType),
                    "Media Foundation camera media type selection failed.");
                return format;
            }
            finally
            {
                ReleaseComObject(nativeType);
            }
        }

        throw WindowsCameraFaults.CreateException(
            WindowsMediaFoundationNative.MF_E_INVALIDMEDIATYPE,
            $"Requested camera format {preferred.Width}x{preferred.Height} {preferred.PixelFormat} {preferred.FrameRateNumerator}/{preferred.FrameRateDenominator} is not available.");
    }

    private CameraFormat NegotiateDefaultFormat(IMFSourceReader reader)
    {
        int fallbackIndex = -1;
        for (int index = 0; ; index++)
        {
            int result = reader.GetNativeMediaType(
                VideoStreamIndex,
                index,
                out IMFMediaType nativeType);
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

                WindowsCameraFaults.ThrowIfFailed(
                    reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType),
                    "Media Foundation camera default media type selection failed.");
                return format;
            }
            finally
            {
                ReleaseComObject(nativeType);
            }
        }

        if (fallbackIndex >= 0)
            return SelectNativeMediaType(reader, fallbackIndex);

        int currentResult = reader.GetCurrentMediaType(
            VideoStreamIndex,
            out IMFMediaType currentType);
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
        WindowsCameraFaults.ThrowIfFailed(
            reader.GetNativeMediaType(VideoStreamIndex, index, out IMFMediaType nativeType),
            "Media Foundation camera default media type lookup failed.");
        try
        {
            WindowsCameraFaults.ThrowIfFailed(
                reader.SetCurrentMediaType(VideoStreamIndex, IntPtr.Zero, nativeType),
                "Media Foundation camera default media type selection failed.");
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
        int result = reader.ReadSample(
            VideoStreamIndex,
            0,
            out _,
            out SourceReaderFlags streamFlags,
            out long timestamp,
            out IMFSample? sample);

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
            _formatChangedCount++;
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
            _discontinuousCount++;

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
        WindowsCameraFaults.ThrowIfFailed(sample.GetBufferCount(out int bufferCount), "Media Foundation camera sample buffer count lookup failed.");
        if (bufferCount <= 0)
        {
            PublishStatistics();
            return;
        }

        WindowsCameraFaults.ThrowIfFailed(sample.ConvertToContiguousBuffer(out IMFMediaBuffer buffer), "Media Foundation camera buffer conversion failed.");
        try
        {
            WindowsCameraFaults.ThrowIfFailed(buffer.Lock(out IntPtr data, out _, out int currentLength), "Media Foundation camera buffer lock failed.");
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

            CameraFrameLease lease = new(
                bytes,
                format,
                WindowsCameraMediaType.CreatePlanes(format, currentLength),
                frameTimestamp,
                _frameNumber++,
                flags);

            _capturedCount++;
            if (_deliveryQueue.TryEnqueue(lease))
                DeliverQueuedFrames();

            PublishStatistics();
        }
        finally
        {
            ReleaseComObject(buffer);
        }
    }

    private void DeliverQueuedFrames()
    {
        while (_deliveryQueue.TryDequeue(out CameraFrameLease? frame))
        {
            if (frame is null)
                continue;

            if (!AreCallbacksEnabled())
            {
                frame.Dispose();
                continue;
            }

            try
            {
                _deliver(frame);
                _deliveredCount++;
            }
            catch (Exception exception)
            {
                frame.Dispose();
                _diagnostics.Write(new InputDiagnosticEvent(
                    InputDiagnosticLevel.Error,
                    "camera.callback.failed",
                    _clock.GetTimestamp(),
                    _descriptor.Id,
                    InputErrorCategory.NativeFailure,
                    new Dictionary<string, string>
                    {
                        ["exception"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["message"] = exception.Message,
                    }));
            }
        }
    }

    private void PublishStatistics()
    {
        _statisticsChanged(new CameraCaptureStatistics(
            _capturedCount,
            _deliveredCount,
            _deliveryQueue.DroppedNewestCount,
            _deliveryQueue.DroppedOldestCount,
            _formatChangedCount,
            _discontinuousCount,
            _deliveryQueue.QueueDepth));
    }

    private bool IsStopRequested()
    {
        lock (_gate)
            return _stopRequested;
    }

    private bool AreCallbacksEnabled()
    {
        lock (_gate)
            return _callbacksEnabled;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WindowsCameraCaptureSession));
    }

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
