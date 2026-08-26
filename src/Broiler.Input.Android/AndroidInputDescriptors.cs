using System.Collections.Generic;
using System.Globalization;

namespace Broiler.Input.Android;

/// <summary>
/// Builds <see cref="InputDeviceDescriptor"/> values for Android input devices.
/// </summary>
/// <remarks>
/// Android identifies devices with an <c>InputDevice</c> id that is only unique within a boot, so
/// the opaque Broiler id is prefixed per kind. A host that enumerates
/// <c>InputDevice.getDeviceIds()</c> passes the real ids; a host that has only the built-in
/// touchscreen can use the <c>Default*</c> descriptors.
/// </remarks>
public static class AndroidInputDescriptors
{
    /// <summary>The device id Android uses for events that are not attributable to a real device.</summary>
    public const int VirtualDeviceId = -1;

    public static InputDeviceDescriptor Touch(
        int androidDeviceId = VirtualDeviceId,
        string? displayName = null,
        bool supportsPressure = false,
        int maximumContacts = 0) =>
        new(
            InputDeviceId.FromOpaqueValue("android:touch:" + androidDeviceId.ToString(CultureInfo.InvariantCulture)),
            InputKind.Touch,
            displayName ?? "Android touch screen",
            InputDeviceAvailability.Available,
            BuildCapabilities(
                ("android.device-id", androidDeviceId.ToString(CultureInfo.InvariantCulture)),
                ("touch.pressure", supportsPressure ? "true" : "false"),
                ("touch.maximum-contacts", maximumContacts > 0 ? maximumContacts.ToString(CultureInfo.InvariantCulture) : "unknown")));

    public static InputDeviceDescriptor Pen(
        int androidDeviceId = VirtualDeviceId,
        string? displayName = null,
        bool supportsPressure = true,
        bool supportsTilt = false,
        bool supportsEraser = false) =>
        new(
            InputDeviceId.FromOpaqueValue("android:pen:" + androidDeviceId.ToString(CultureInfo.InvariantCulture)),
            InputKind.Pen,
            displayName ?? "Android stylus",
            InputDeviceAvailability.Available,
            BuildCapabilities(
                ("android.device-id", androidDeviceId.ToString(CultureInfo.InvariantCulture)),
                ("pen.pressure", supportsPressure ? "true" : "false"),
                ("pen.tilt", supportsTilt ? "true" : "false"),
                ("pen.eraser", supportsEraser ? "true" : "false")));

    public static InputDeviceDescriptor Keyboard(
        int androidDeviceId = VirtualDeviceId,
        string? displayName = null,
        bool isPhysical = false) =>
        new(
            InputDeviceId.FromOpaqueValue("android:keyboard:" + androidDeviceId.ToString(CultureInfo.InvariantCulture)),
            InputKind.Keyboard,
            displayName ?? (isPhysical ? "Android hardware keyboard" : "Android keyboard"),
            InputDeviceAvailability.Available,
            BuildCapabilities(
                ("android.device-id", androidDeviceId.ToString(CultureInfo.InvariantCulture)),
                ("keyboard.physical", isPhysical ? "true" : "false")));

    /// <summary>
    /// The IME text device. It uses a keyboard descriptor because
    /// <c>Broiler.Input.Text.TextInputDevice</c> requires one.
    /// </summary>
    public static InputDeviceDescriptor Text(string? displayName = null) =>
        new(
            InputDeviceId.FromOpaqueValue("android:text:ime"),
            InputKind.Keyboard,
            displayName ?? "Android input method",
            InputDeviceAvailability.Available,
            BuildCapabilities(
                ("text.composition", "true"),
                ("text.source", "android.view.inputmethod.InputConnection")));

    private static IEnumerable<InputCapability> BuildCapabilities(params (string Name, string Value)[] capabilities)
    {
        foreach ((string name, string value) in capabilities)
            yield return new InputCapability(name, value);
    }
}
