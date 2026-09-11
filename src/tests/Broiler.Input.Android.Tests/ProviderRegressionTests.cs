using System;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Android;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Android;

namespace Broiler.Input.Android.Tests;

internal static class ProviderRegressionTests
{
    public static async Task ReopenDisposedDevice()
    {
        AndroidKeyboardProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultKeyboard();
        KeyboardInputDevice first = await provider.OpenAsync(descriptor, new KeyboardOpenOptions());
        first.Dispose();
        Require(!provider.TryGetOpenDevice(descriptor.Id, out _), "Disposed devices must be evicted.");
        using KeyboardInputDevice second = await provider.OpenAsync(descriptor, new KeyboardOpenOptions());
        Require(!ReferenceEquals(first, second), "Reopening returned the disposed instance.");
        await second.OpenAsync();
        await second.StartAsync();
        Require(second.State == InputDeviceState.Running, "Replacement device is unusable.");
    }

    public static async Task RemoveDisposedDevice()
    {
        AndroidKeyboardProvider provider = new();
        InputDeviceDescriptor descriptor = provider.RegisterDefaultKeyboard();
        KeyboardInputDevice device = await provider.OpenAsync(descriptor, new KeyboardOpenOptions());
        device.Dispose();
        bool removed = false;
        provider.DeviceChanged += change => removed |= change.Kind == InputDeviceChangeKind.Removed;
        provider.NotifyCaptureLost();
        Require(provider.RemoveDevice(descriptor.Id), "Registered device was not removed.");
        Require(removed, "Removal notification was lost.");
    }

    public static async Task RemovalDuringOpen()
    {
        using ManualResetEventSlim entered = new(), release = new();
        BlockingProvider provider = new(entered, release);
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Keyboard();
        provider.RegisterDevice(descriptor);
        Task<KeyboardInputDevice> opening = Task.Run(async () => await provider.OpenAsync(descriptor, new KeyboardOpenOptions()));
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "Device construction did not start.");
            provider.RemoveDevice(descriptor.Id);
        }
        finally { release.Set(); }
        bool rejected = false;
        try { await opening.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "A removed device was cached during construction.");
        Require(provider.Created?.State == InputDeviceState.Disposed, "Rejected construction leaked its device.");
        Require(!provider.TryGetOpenDevice(descriptor.Id, out _), "Removed device remains in the cache.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class BlockingProvider(ManualResetEventSlim entered, ManualResetEventSlim release)
        : AndroidInputProvider<KeyboardInputDevice, KeyboardOpenOptions>
    {
        public KeyboardInputDevice? Created { get; private set; }
        protected override KeyboardInputDevice CreateDevice(InputDeviceDescriptor descriptor, KeyboardOpenOptions options)
        {
            Created = new AndroidKeyboardInputDevice(descriptor, Clock, options);
            entered.Set();
            release.Wait();
            return Created;
        }
    }
}
