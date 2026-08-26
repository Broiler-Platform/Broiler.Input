using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input;
using Broiler.Input.Microphone;

namespace Broiler.Input.Microphone.Windows;

internal sealed class WindowsMicrophoneCaptureSession : IDisposable, IAsyncDisposable
{
    private const int WasapiQpcFrequency = 10_000_000;
    private const ushort WaveFormatPcm = 0x0001;
    private const ushort WaveFormatIeeeFloat = 0x0003;
    private const ushort WaveFormatExtensible = 0xFFFE;

    private readonly object _gate = new();
    private readonly InputDeviceDescriptor _descriptor;
    private readonly MicrophoneOpenOptions _options;
    private readonly Action<MicrophoneBufferLease> _deliver;
    private readonly Action<InputFault> _invalidated;
    private readonly Action<MicrophoneCaptureStatistics> _statisticsChanged;
    private readonly IInputClock _clock;
    private readonly IInputDiagnosticSink _diagnostics;
    private readonly WindowsMicrophoneDeliveryQueue _deliveryQueue;

    private Thread? _thread;
    private TaskCompletionSource<object?>? _started;
    private IntPtr _eventHandle;
    private bool _stopRequested;
    private bool _callbacksEnabled;
    private bool _disposed;
    private long _capturedCount;
    private long _deliveredCount;
    private long _silentCount;
    private long _discontinuousCount;

    public WindowsMicrophoneCaptureSession(
        InputDeviceDescriptor descriptor,
        MicrophoneOpenOptions options,
        Action<MicrophoneBufferLease> deliver,
        Action<InputFault> invalidated,
        Action<MicrophoneCaptureStatistics> statisticsChanged,
        IInputClock clock,
        IInputDiagnosticSink? diagnostics)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        _invalidated = invalidated ?? throw new ArgumentNullException(nameof(invalidated));
        _statisticsChanged = statisticsChanged ?? throw new ArgumentNullException(nameof(statisticsChanged));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        _deliveryQueue = new WindowsMicrophoneDeliveryQueue(_options.SessionOptions.DeliveryOptions);
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
            _silentCount = 0;
            _discontinuousCount = 0;
            _started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            started = _started;
            _thread = new Thread(RunCapture)
            {
                IsBackground = true,
                Name = "Broiler.Input.Microphone.Windows.WASAPI",
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
        lock (_gate)
        {
            _callbacksEnabled = false;
            _stopRequested = true;
            if (_eventHandle != IntPtr.Zero)
                WindowsWasapiNative.SetEvent(_eventHandle);

            thread = _thread;
        }

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
        using WindowsComApartmentScope apartment = WindowsComApartmentScope.Enter();
        object? enumeratorObject = null;
        IMMDevice? endpoint = null;
        object? audioClientObject = null;
        IAudioClient? audioClient = null;
        object? captureClientObject = null;
        IAudioCaptureClient? captureClient = null;
        IntPtr mixFormatPointer = IntPtr.Zero;
        bool audioStarted = false;

        try
        {
            endpoint = WindowsMicrophoneEndpointEnumerator.GetDevice(_descriptor, _options.Role, out enumeratorObject);
            audioClient = ActivateAudioClient(endpoint, out audioClientObject);
            MicrophoneFormat actualFormat = GetMixFormat(audioClient, out mixFormatPointer);
            ValidatePreferredFormat(actualFormat);

            _eventHandle = WindowsWasapiNative.CreateEventW(IntPtr.Zero, manualReset: false, initialState: false, name: null);
            if (_eventHandle == IntPtr.Zero)
                throw WindowsMicrophoneFaults.CreateException(Marshal.GetHRForLastWin32Error(), "WASAPI capture event creation failed.");

            WindowsMicrophoneFaults.ThrowIfFailed(
                audioClient.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.EventCallback,
                    _options.SessionOptions.RequestedLatency.Ticks,
                    0,
                    mixFormatPointer,
                    IntPtr.Zero),
                "WASAPI shared-mode microphone capture initialization failed.");
            WindowsMicrophoneFaults.ThrowIfFailed(audioClient.SetEventHandle(_eventHandle), "WASAPI event binding failed.");

            Guid captureClientId = WindowsWasapiNative.IAudioCaptureClientId;
            WindowsMicrophoneFaults.ThrowIfFailed(
                audioClient.GetService(ref captureClientId, out captureClientObject),
                "WASAPI capture client activation failed.");
            captureClient = captureClientObject as IAudioCaptureClient
                ?? throw WindowsMicrophoneFaults.CreateException(unchecked((int)0x80004002), "WASAPI capture client interface activation failed.");

            WindowsMicrophoneFaults.ThrowIfFailed(audioClient.Start(), "WASAPI microphone capture start failed.");
            audioStarted = true;
            _started?.TrySetResult(null);

            while (!IsStopRequested())
            {
                uint waitResult = WindowsWasapiNative.WaitForSingleObject(_eventHandle, 250);
                if (waitResult == WindowsWasapiNative.WAIT_TIMEOUT)
                    continue;
                if (waitResult == WindowsWasapiNative.WAIT_FAILED)
                    throw WindowsMicrophoneFaults.CreateException(Marshal.GetHRForLastWin32Error(), "WASAPI capture wait failed.");

                ReadAvailablePackets(captureClient, actualFormat);
            }
        }
        catch (InputMicrophoneException exception)
        {
            _started?.TrySetException(exception);
            if (exception.Fault.Category == InputErrorCategory.DeviceRemoved && AreCallbacksEnabled())
                _invalidated(exception.Fault);
        }
        catch (Exception exception)
        {
            InputFault fault = new(
                InputErrorCategory.NativeFailure,
                "WASAPI microphone capture failed.",
                exception,
                nativeFacility: "WASAPI");
            _started?.TrySetException(new InputMicrophoneException(fault));
        }
        finally
        {
            if (audioStarted && audioClient is not null)
                audioClient.Stop();

            if (_eventHandle != IntPtr.Zero)
            {
                WindowsWasapiNative.CloseHandle(_eventHandle);
                _eventHandle = IntPtr.Zero;
            }

            if (mixFormatPointer != IntPtr.Zero)
                WindowsWasapiNative.CoTaskMemFree(mixFormatPointer);

            _deliveryQueue.DisposeAll();
            PublishStatistics();
            ReleaseComObject(captureClientObject);
            ReleaseComObject(audioClientObject);
            ReleaseComObject(endpoint);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static IAudioClient ActivateAudioClient(IMMDevice endpoint, out object? audioClientObject)
    {
        Guid audioClientId = WindowsWasapiNative.IAudioClientId;
        WindowsMicrophoneFaults.ThrowIfFailed(
            endpoint.Activate(
                ref audioClientId,
                WindowsWasapiNative.CLSCTX_INPROC_SERVER,
                IntPtr.Zero,
                out audioClientObject),
            "WASAPI audio client activation failed.");

        return audioClientObject as IAudioClient
            ?? throw WindowsMicrophoneFaults.CreateException(unchecked((int)0x80004002), "WASAPI audio client interface activation failed.");
    }

    private static MicrophoneFormat GetMixFormat(IAudioClient audioClient, out IntPtr formatPointer)
    {
        WindowsMicrophoneFaults.ThrowIfFailed(audioClient.GetMixFormat(out formatPointer), "WASAPI mix-format lookup failed.");
        WaveFormatEx waveFormat = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);

        ushort bitsPerSample = waveFormat.BitsPerSample;
        ushort formatTag = waveFormat.FormatTag;
        Guid subFormat = Guid.Empty;

        if (waveFormat.FormatTag == WaveFormatExtensible)
        {
            WaveFormatExtensible extensible = Marshal.PtrToStructure<WaveFormatExtensible>(formatPointer);
            bitsPerSample = extensible.ValidBitsPerSample == 0
                ? extensible.Format.BitsPerSample
                : extensible.ValidBitsPerSample;
            formatTag = extensible.Format.FormatTag;
            subFormat = extensible.SubFormat;
        }

        MicrophoneSampleFormat sampleFormat = ToSampleFormat(formatTag, bitsPerSample, subFormat);
        return new MicrophoneFormat(
            checked((int)waveFormat.SamplesPerSec),
            waveFormat.Channels,
            bitsPerSample,
            sampleFormat);
    }

    private static MicrophoneSampleFormat ToSampleFormat(ushort formatTag, ushort bitsPerSample, Guid subFormat)
    {
        if (formatTag == WaveFormatIeeeFloat || subFormat == WindowsWasapiNative.IeeeFloatSubFormat)
            return bitsPerSample == 32 ? MicrophoneSampleFormat.Float32 : MicrophoneSampleFormat.Unknown;

        if (formatTag == WaveFormatPcm || subFormat == WindowsWasapiNative.PcmSubFormat)
        {
            return bitsPerSample switch
            {
                16 => MicrophoneSampleFormat.Pcm16,
                24 => MicrophoneSampleFormat.Pcm24,
                32 => MicrophoneSampleFormat.Pcm32,
                _ => MicrophoneSampleFormat.Unknown,
            };
        }

        return MicrophoneSampleFormat.Unknown;
    }

    private void ValidatePreferredFormat(MicrophoneFormat actualFormat)
    {
        MicrophoneFormat? preferred = _options.PreferredFormat;
        if (preferred is null)
            return;

        if (preferred.SampleRate == actualFormat.SampleRate &&
            preferred.ChannelCount == actualFormat.ChannelCount &&
            preferred.BitsPerSample == actualFormat.BitsPerSample &&
            preferred.SampleFormat == actualFormat.SampleFormat)
        {
            return;
        }

        throw WindowsMicrophoneFaults.CreateException(
            WindowsWasapiNative.AUDCLNT_E_UNSUPPORTED_FORMAT,
            $"Requested microphone format {preferred.SampleRate} Hz/{preferred.ChannelCount} ch/{preferred.BitsPerSample} bit is not the shared-mode mix format.");
    }

    private void ReadAvailablePackets(IAudioCaptureClient captureClient, MicrophoneFormat format)
    {
        while (!IsStopRequested())
        {
            int packetResult = captureClient.GetNextPacketSize(out uint framesInNextPacket);
            if (packetResult == WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED)
                throw WindowsMicrophoneFaults.CreateException(packetResult, "The microphone endpoint was invalidated.");
            WindowsMicrophoneFaults.ThrowIfFailed(packetResult, "WASAPI capture packet-size query failed.");

            if (framesInNextPacket == 0)
                break;

            int bufferResult = captureClient.GetBuffer(
                out IntPtr data,
                out uint framesToRead,
                out AudioClientBufferFlags flags,
                out ulong devicePosition,
                out ulong qpcPosition);
            if (bufferResult == WindowsWasapiNative.AUDCLNT_E_DEVICE_INVALIDATED)
                throw WindowsMicrophoneFaults.CreateException(bufferResult, "The microphone endpoint was invalidated.");
            WindowsMicrophoneFaults.ThrowIfFailed(bufferResult, "WASAPI capture packet read failed.");

            try
            {
                CapturePacket(data, framesToRead, flags, devicePosition, qpcPosition, format);
            }
            finally
            {
                WindowsMicrophoneFaults.ThrowIfFailed(
                    captureClient.ReleaseBuffer(framesToRead),
                    "WASAPI capture packet release failed.");
            }
        }
    }

    private void CapturePacket(
        IntPtr data,
        uint framesToRead,
        AudioClientBufferFlags flags,
        ulong devicePosition,
        ulong qpcPosition,
        MicrophoneFormat format)
    {
        bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
        bool discontinuous = (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
        bool timestampError = (flags & AudioClientBufferFlags.TimestampError) != 0;

        if (silent && !_options.SessionOptions.ReportSilence)
            return;

        int byteCount = checked((int)framesToRead * format.BytesPerFrame);
        byte[] buffer = new byte[byteCount];
        if (!silent && data != IntPtr.Zero && byteCount > 0)
            Marshal.Copy(data, buffer, 0, byteCount);

        MicrophoneBufferFlags microphoneFlags = MicrophoneBufferFlags.None;
        if (silent)
            microphoneFlags |= MicrophoneBufferFlags.Silent;
        if (discontinuous && _options.SessionOptions.ReportDiscontinuities)
            microphoneFlags |= MicrophoneBufferFlags.Discontinuous;
        if (timestampError)
            microphoneFlags |= MicrophoneBufferFlags.TimestampError;

        InputTimestamp timestamp = qpcPosition > 0 && !timestampError
            ? new InputTimestamp(checked((long)Math.Min(qpcPosition, (ulong)long.MaxValue)), WasapiQpcFrequency, "Windows.WASAPI.QPC")
            : _clock.GetTimestamp();

        MicrophoneBufferLease lease = new(
            buffer,
            format,
            timestamp,
            checked((long)Math.Min(devicePosition, (ulong)long.MaxValue)),
            microphoneFlags);

        _capturedCount++;
        if (silent)
            _silentCount++;
        if (discontinuous)
            _discontinuousCount++;

        if (_deliveryQueue.TryEnqueue(lease))
            DeliverQueuedBuffers();

        PublishStatistics();
    }

    private void DeliverQueuedBuffers()
    {
        while (_deliveryQueue.TryDequeue(out MicrophoneBufferLease? lease))
        {
            if (lease is null)
                continue;

            if (!AreCallbacksEnabled())
            {
                lease.Dispose();
                continue;
            }

            try
            {
                _deliver(lease);
                _deliveredCount++;
            }
            catch (Exception exception)
            {
                lease.Dispose();
                _diagnostics.Write(new InputDiagnosticEvent(
                    InputDiagnosticLevel.Error,
                    "microphone.callback.failed",
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
        _statisticsChanged(new MicrophoneCaptureStatistics(
            _capturedCount,
            _deliveredCount,
            _deliveryQueue.DroppedNewestCount,
            _deliveryQueue.DroppedOldestCount,
            _silentCount,
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
            throw new ObjectDisposedException(nameof(WindowsMicrophoneCaptureSession));
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
