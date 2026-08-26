using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input;

public abstract class InputDevice : IDisposable, IAsyncDisposable
{
    private readonly IInputClock _clock;
    private long _sequenceNumber;
    private bool _disposed;

    protected InputDevice(
        InputDeviceDescriptor descriptor,
        IInputClock? clock = null,
        IInputDiagnosticSink? diagnostics = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _clock = clock ?? StopwatchInputClock.Shared;
        Diagnostics = diagnostics ?? NullInputDiagnosticSink.Shared;
        State = InputDeviceState.Discovered;
    }

    public event EventHandler<InputDeviceStateChangedEventArgs>? StateChanged;

    public InputDeviceDescriptor Descriptor { get; }

    public InputDeviceId Id => Descriptor.Id;

    public InputKind Kind => Descriptor.Kind;

    public string DisplayName => Descriptor.DisplayName;

    public InputDeviceState State { get; private set; }

    public InputFault? LastFault { get; private set; }

    protected IInputClock Clock => _clock;

    protected IInputDiagnosticSink Diagnostics { get; }

    protected bool CanDeliverInput => !_disposed && State == InputDeviceState.Running;

    public virtual ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State is InputDeviceState.Open or InputDeviceState.Running)
            return ValueTask.CompletedTask;

        TransitionTo(InputDeviceState.Opening);
        TransitionTo(InputDeviceState.Open);
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
            return ValueTask.CompletedTask;

        if (State is InputDeviceState.Discovered or InputDeviceState.Closed)
            throw new InvalidOperationException("The input device must be open before it can be started.");

        TransitionTo(InputDeviceState.Starting);
        TransitionTo(InputDeviceState.Running);
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State != InputDeviceState.Running)
            return ValueTask.CompletedTask;

        TransitionTo(InputDeviceState.Stopping);
        TransitionTo(InputDeviceState.Open);
        return ValueTask.CompletedTask;
    }

    public virtual ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (State == InputDeviceState.Running)
        {
            TransitionTo(InputDeviceState.Stopping);
            TransitionTo(InputDeviceState.Open);
        }

        if (State != InputDeviceState.Closed)
            TransitionTo(InputDeviceState.Closed);

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (State == InputDeviceState.Running)
        {
            TransitionTo(InputDeviceState.Stopping);
            TransitionTo(InputDeviceState.Open);
        }

        TransitionTo(InputDeviceState.Disposed);
        _disposed = true;
    }

    protected InputEventHeader NextEventHeader(InputTimestamp? timestamp = null)
    {
        long sequence = Interlocked.Increment(ref _sequenceNumber);
        return new InputEventHeader(Id, timestamp is { IsValid: true } value ? value : _clock.GetTimestamp(), sequence);
    }

    protected void TransitionTo(InputDeviceState state)
    {
        if (State == state)
            return;

        InputDeviceState previous = State;
        State = state;
        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.device.state",
            new Dictionary<string, string>
            {
                ["previous"] = previous.ToString(),
                ["current"] = state.ToString(),
            });
        StateChanged?.Invoke(this, new InputDeviceStateChangedEventArgs(previous, state, _clock.GetTimestamp()));
    }

    protected void SetFault(InputFault fault)
    {
        LastFault = fault ?? throw new ArgumentNullException(nameof(fault));
        EmitDiagnostic(
            InputDiagnosticLevel.Error,
            "input.device.fault",
            new Dictionary<string, string>
            {
                ["category"] = fault.Category.ToString(),
                ["message"] = fault.Message,
            },
            fault.Category);
        TransitionTo(InputDeviceState.Faulted);
    }

    protected void MarkUnavailable(InputFault? fault = null)
    {
        LastFault = fault;
        if (fault is not null)
        {
            EmitDiagnostic(
                InputDiagnosticLevel.Warning,
                "input.device.unavailable",
                new Dictionary<string, string>
                {
                    ["category"] = fault.Category.ToString(),
                    ["message"] = fault.Message,
                },
                fault.Category);
        }

        TransitionTo(InputDeviceState.Unavailable);
    }

    protected void EmitDiagnostic(
        InputDiagnosticLevel level,
        string name,
        IReadOnlyDictionary<string, string>? properties = null,
        InputErrorCategory? errorCategory = null)
    {
        Diagnostics.Write(new InputDiagnosticEvent(
            level,
            name,
            _clock.GetTimestamp(),
            Id,
            errorCategory,
            properties));
    }

    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
    }
}
