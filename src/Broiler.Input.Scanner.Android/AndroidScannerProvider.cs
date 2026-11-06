using System;
using Broiler.Input.Android;

namespace Broiler.Input.Scanner.Android;

/// <summary>
/// Opens <see cref="AndroidScannerInputDevice"/> instances for document or image scanners registered by the Android host.
/// </summary>
public sealed class AndroidScannerProvider(AndroidUptimeInputClock? clock = null)
    : AndroidInputProvider<ScannerInputDevice, ScannerOpenOptions>(clock), IScannerInputProvider
{
    /// <summary>
    /// Registers a default Android document scanner and returns its descriptor.
    /// </summary>
    public InputDeviceDescriptor RegisterDefaultScanner(
        string? displayName = null,
        bool supportsFeeder = false,
        bool supportsDuplex = false)
    {
        InputDeviceDescriptor descriptor = AndroidInputDescriptors.Scanner(
            displayName: displayName,
            supportsFeeder: supportsFeeder,
            supportsDuplex: supportsDuplex);
        RegisterDevice(descriptor);
        return descriptor;
    }

    protected override ScannerInputDevice CreateDevice(InputDeviceDescriptor descriptor, ScannerOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new AndroidScannerInputDevice(descriptor, Clock, options);
    }
}
