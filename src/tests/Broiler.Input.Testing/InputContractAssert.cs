using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Testing;

public static class InputContractAssert
{
    public static async Task ProvesLifecycleAsync(FakeInputProvider provider)
    {
        InputDeviceDescriptor descriptor = await GetFirstDescriptorAsync(provider).ConfigureAwait(false);
        FakeInputDevice device = await provider.OpenAsync(descriptor, new FakeInputOpenOptions()).ConfigureAwait(false);

        Equal(InputDeviceState.Discovered, device.State, "New fake devices start discovered.");

        await device.OpenAsync().ConfigureAwait(false);
        Equal(InputDeviceState.Open, device.State, "OpenAsync transitions to Open.");

        await device.StartAsync().ConfigureAwait(false);
        Equal(InputDeviceState.Running, device.State, "StartAsync transitions to Running.");

        await device.StopAsync().ConfigureAwait(false);
        Equal(InputDeviceState.Open, device.State, "StopAsync returns to Open.");

        await device.CloseAsync().ConfigureAwait(false);
        Equal(InputDeviceState.Closed, device.State, "CloseAsync transitions to Closed.");

        await device.DisposeAsync().ConfigureAwait(false);
        Equal(InputDeviceState.Disposed, device.State, "DisposeAsync transitions to Disposed.");
    }

    public static async Task ProvesCancellationAsync(FakeInputProvider provider)
    {
        InputDeviceDescriptor descriptor = await GetFirstDescriptorAsync(provider).ConfigureAwait(false);
        using CancellationTokenSource source = new();
        source.Cancel();

        try
        {
            await provider.OpenAsync(descriptor, new FakeInputOpenOptions(), source.Token).ConfigureAwait(false);
            throw new InvalidOperationException("Canceled open did not throw.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    public static async Task ProvesRemovalAsync(FakeInputProvider provider)
    {
        InputDeviceDescriptor descriptor = await GetFirstDescriptorAsync(provider).ConfigureAwait(false);
        FakeInputDevice device = await provider.OpenAsync(descriptor, new FakeInputOpenOptions()).ConfigureAwait(false);
        await device.OpenAsync().ConfigureAwait(false);
        await device.StartAsync().ConfigureAwait(false);

        InputDeviceChange? observed = null;
        provider.DeviceChanged += change => observed = change;

        True(provider.RemoveDevice(descriptor.Id), "Provider removes the descriptor.");
        Equal(InputDeviceState.Unavailable, device.State, "Removal marks open devices unavailable.");
        True(device.LastFault?.Category == InputErrorCategory.DeviceRemoved, "Removal records a device-removed fault.");
        True(observed?.Kind == InputDeviceChangeKind.Removed, "Provider emits a removed device change.");
    }

    public static async Task ProvesBoundedDeliveryAsync(FakeInputProvider provider)
    {
        InputDeviceDescriptor descriptor = await GetFirstDescriptorAsync(provider).ConfigureAwait(false);
        FakeInputOpenOptions options = new(new InputDeliveryOptions(2, InputDeliveryOverflowPolicy.DropOldest));
        FakeInputDevice device = await provider.OpenAsync(descriptor, options).ConfigureAwait(false);

        True(device.TryDeliver("one"), "First item enqueues.");
        True(device.TryDeliver("two"), "Second item enqueues.");
        True(device.TryDeliver("three"), "Third item drops the oldest item.");

        InputDeliveryMetrics metrics = device.DeliveryMetrics;
        Equal(3L, metrics.EnqueuedCount, "All accepted enqueue attempts are counted.");
        Equal(1L, metrics.DroppedOldestCount, "One old item is dropped.");
        Equal(2, metrics.QueueDepth, "Queue depth stays bounded.");

        True(device.TryRead(out string? first), "First bounded item is readable.");
        Equal("two", first, "The oldest item was dropped.");
        True(device.TryRead(out string? second), "Second bounded item is readable.");
        Equal("three", second, "The newest item is retained.");
    }

    public static async Task ProvesDiagnosticsAsync()
    {
        ManualInputClock clock = new();
        RecordingInputDiagnosticSink diagnostics = new();
        FakeInputProvider provider = new(clock, diagnostics);
        InputDeviceDescriptor descriptor = await GetFirstDescriptorAsync(provider).ConfigureAwait(false);
        FakeInputDevice device = await provider.OpenAsync(descriptor, new FakeInputOpenOptions()).ConfigureAwait(false);

        await device.OpenAsync().ConfigureAwait(false);
        device.SimulateRemoval();

        True(diagnostics.Events.Any(static inputEvent => inputEvent.Name == "input.device.state"),
            "Lifecycle transitions emit diagnostics.");
        True(diagnostics.Events.Any(static inputEvent => inputEvent.ErrorCategory == InputErrorCategory.DeviceRemoved),
            "Removal emits a device-removed diagnostic.");
    }

    private static async Task<InputDeviceDescriptor> GetFirstDescriptorAsync(FakeInputProvider provider)
    {
        IReadOnlyList<InputDeviceDescriptor> descriptors = await provider.GetDevicesAsync().ConfigureAwait(false);
        True(descriptors.Count > 0, "Provider returns at least one descriptor.");
        return descriptors.First();
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
}
