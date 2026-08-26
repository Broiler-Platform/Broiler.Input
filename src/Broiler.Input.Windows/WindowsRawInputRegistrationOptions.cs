using System;

namespace Broiler.Input.Windows;

public sealed record WindowsRawInputRegistrationOptions(
    bool ReceiveInputWhenNotFocused = false,
    bool SuppressLegacyMessages = false,
    bool ObserveDeviceChanges = true,
    bool AcknowledgeBackgroundInput = false)
{
    public void Validate(IntPtr targetWindow)
    {
        if (!ReceiveInputWhenNotFocused)
            return;

        if (targetWindow == IntPtr.Zero)
            throw new ArgumentException("Background Raw Input registration requires a target window.", nameof(targetWindow));

        if (!AcknowledgeBackgroundInput)
            throw new InvalidOperationException("Background Raw Input requires explicit acknowledgement.");
    }
}
