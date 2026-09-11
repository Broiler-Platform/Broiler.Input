using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Broiler.Input.Linux;

namespace Broiler.Input.Mouse.Linux;

public sealed class LinuxMouseInputDevice : MouseInputDevice
{
    private readonly MouseOpenOptions _options;
    private readonly string _eventPath;
    private readonly string _eventName;
    private readonly int _pollTimeoutMilliseconds;
    private readonly LinuxPointerMotionMode _motionMode;
    private LinuxMouseEventTranslator _translator = new();
    private LinuxEventDeviceStream? _stream;
    private readonly LinuxReadLoopSession _readLoop = new();

    public LinuxMouseInputDevice(InputDeviceDescriptor descriptor, MouseOpenOptions options, string eventPath, string eventName,
        int pollTimeoutMilliseconds, IInputClock? clock = null, LinuxPointerMotionMode motionMode = LinuxPointerMotionMode.Relative)
        : base(descriptor, clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        ArgumentException.ThrowIfNullOrWhiteSpace(eventPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        _eventPath = eventPath;
        _eventName = eventName;
        _pollTimeoutMilliseconds = pollTimeoutMilliseconds <= 0 ? 50 : pollTimeoutMilliseconds;
        _motionMode = motionMode;
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
            _translator = CreateTranslator(stream);
            _stream = stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private LinuxMouseEventTranslator CreateTranslator(LinuxEventDeviceStream stream)
    {
        if (_motionMode != LinuxPointerMotionMode.AbsoluteTouchpad)
            return new LinuxMouseEventTranslator();

        LinuxAbsAxis x = ReadAxis(stream, LinuxEvdevConstants.AbsX, LinuxEvdevConstants.AbsMtPositionX);
        LinuxAbsAxis y = ReadAxis(stream, LinuxEvdevConstants.AbsY, LinuxEvdevConstants.AbsMtPositionY);
        return new LinuxMouseEventTranslator(LinuxPointerMotionMode.AbsoluteTouchpad, x, y);
    }

    private static LinuxAbsAxis ReadAxis(LinuxEventDeviceStream stream, ushort primary, ushort multitouch)
    {
        if (stream.TryGetAbsoluteAxis(primary, out int min, out int max, out int resolution) && max > min)
            return new LinuxAbsAxis(min, max, resolution);

        if (stream.TryGetAbsoluteAxis(multitouch, out min, out max, out resolution) && max > min)
            return new LinuxAbsAxis(min, max, resolution);

        return default;
    }

    public override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return;

        if (_stream is null)
            throw new InvalidOperationException("The Linux mouse event device must be open before it can be started.");

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
        
        LinuxEventDeviceReadLoop loop = new(_stream, _pollTimeoutMilliseconds);
        _readLoop.Start(token => loop.Run(ProcessInputEvent, HandleReadFault, token), cancellationToken);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _readLoop.StopAsync().ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _readLoop.StopAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _stream = null;
        await base.CloseAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        await _readLoop.StopAsync().ConfigureAwait(false);
        _stream?.Dispose();
        _stream = null;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _readLoop.StopAsync().AsTask().GetAwaiter().GetResult();
            _stream?.Dispose();
            _stream = null;
        }

        base.Dispose(disposing);
    }

    private void ProcessInputEvent(LinuxInputEvent inputEvent)
    {
        List<LinuxMouseTranslatedEvent> events = [];
        _translator.Process(inputEvent, timestamp => NextEventHeader(timestamp), _options, events);
        
        foreach (LinuxMouseTranslatedEvent input in events)
        {
            switch (input.Kind)
            {
                case LinuxMouseTranslatedEventKind.Move when input.Move is { } moved:
                    RaiseMoved(moved);
                    break;
                case LinuxMouseTranslatedEventKind.Button when input.Button is { } button:
                    RaiseButtonChanged(button);
                    break;
                case LinuxMouseTranslatedEventKind.Wheel when input.Wheel is { } wheel:
                    RaiseWheelChanged(wheel);
                    break;
            }
        }
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

}
