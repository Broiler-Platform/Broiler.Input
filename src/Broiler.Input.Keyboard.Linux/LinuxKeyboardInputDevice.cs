using System;
using System.Threading.Tasks;
using System.Threading;
using Broiler.Input.Linux;

namespace Broiler.Input.Keyboard.Linux;

public sealed class LinuxKeyboardInputDevice : KeyboardInputDevice
{
    private readonly string _eventPath;
    private readonly string _eventName;
    private readonly int _pollTimeoutMilliseconds;
    private readonly LinuxKeyboardEventTranslator _translator = new();
    private LinuxEventDeviceStream? _stream;
    private CancellationTokenSource? _readCancellation;
    private Task? _readTask;

    public LinuxKeyboardInputDevice(
        InputDeviceDescriptor descriptor,
        string eventPath,
        string eventName,
        int pollTimeoutMilliseconds,
        IInputClock? clock = null)
        : base(descriptor, clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        _eventPath = eventPath;
        _eventName = eventName;
        _pollTimeoutMilliseconds = pollTimeoutMilliseconds <= 0 ? 50 : pollTimeoutMilliseconds;
    }

    public override async ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State is InputDeviceState.Open or InputDeviceState.Running)
            return;

        LinuxEventDeviceStream stream = LinuxEventDeviceStream.Open(_eventPath, _eventName);
        try
        {
            await base.OpenAsync(cancellationToken).ConfigureAwait(false);
            _stream = stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (_stream is null)
            throw new InvalidOperationException("The Linux keyboard event device must be open before it can be started.");

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        _readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        LinuxEventDeviceReadLoop loop = new(_stream, _pollTimeoutMilliseconds);
        _readTask = Task.Run(
            () => loop.Run(ProcessInputEvent, HandleReadFault, _readCancellation.Token),
            CancellationToken.None);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopReadLoopAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await StopReadLoopAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _stream = null;
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await StopReadLoopAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _stream = null;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopReadLoopAsync().AsTask().GetAwaiter().GetResult();
            _stream?.Dispose();
            _stream = null;
        }

        base.Dispose(disposing);
    }

    private void ProcessInputEvent(LinuxInputEvent inputEvent)
    {
        if (_translator.TryTranslate(inputEvent, timestamp => NextEventHeader(timestamp), out KeyboardKeyEvent keyboardEvent))
            RaiseKeyChanged(keyboardEvent);
    }

    private void HandleReadFault(InputFault fault)
    {
        if (fault.Category == InputErrorCategory.DeviceRemoved)
            MarkUnavailable(fault);
        else if (fault.Category == InputErrorCategory.CaptureDiscontinuity)
            EmitDiagnostic(InputDiagnosticLevel.Warning, "input.linux.evdev.syn_dropped", errorCategory: fault.Category);
        else
            SetFault(fault);
    }

    private async ValueTask StopReadLoopAsync()
    {
        CancellationTokenSource? cancellation = _readCancellation;
        Task? task = _readTask;
        _readCancellation = null;
        _readTask = null;

        if (cancellation is null)
            return;

        cancellation.Cancel();
        try
        {
            if (task is not null)
                await task.ConfigureAwait(false);
        }
        finally
        {
            cancellation.Dispose();
        }
    }
}
