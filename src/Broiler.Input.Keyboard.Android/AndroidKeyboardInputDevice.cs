using System;
using System.Collections.Generic;
using Broiler.Input.Android;

namespace Broiler.Input.Keyboard.Android;

/// <summary>
/// A <see cref="KeyboardInputDevice"/> driven by Android <c>KeyEvent</c> data.
/// </summary>
/// <remarks>
/// This device handles keys: hardware keyboards, the D-pad, and the discrete keys a soft keyboard
/// sends. It is not the IME path — composed text arrives through
/// <c>Broiler.Input.Text.Android</c>, because an input method commits text through
/// <c>InputConnection</c> without ever synthesizing a key event. A host wires both and lets each
/// own its half.
/// </remarks>
public sealed class AndroidKeyboardInputDevice : KeyboardInputDevice, IAndroidInputDevice
{
    private readonly AndroidUptimeInputClock _clock;
    private readonly KeyboardOpenOptions _options;
    private readonly HashSet<int> _downKeys = [];

    public AndroidKeyboardInputDevice(
        InputDeviceDescriptor descriptor,
        AndroidUptimeInputClock clock,
        KeyboardOpenOptions? options = null)
        : base(descriptor, clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new KeyboardOpenOptions();
    }

    /// <summary>The Android key codes currently held down.</summary>
    public IReadOnlyCollection<int> DownKeys => _downKeys;

    /// <summary>
    /// Translates one <c>KeyEvent</c>, raising a key event and — when the key produces a character
    /// and text delivery is enabled — a text event.
    /// </summary>
    /// <returns>True when the event produced at least one input event.</returns>
    public bool ProcessKeyEvent(in AndroidKeyEventSample sample)
    {
        ThrowIfDisposed();

        _clock.Observe(sample.EventTimeMilliseconds);

        if (!CanDeliverInput)
            return false;

        return sample.Action switch
        {
            AndroidKeyEventConstants.ActionDown => ProcessTransition(sample, KeyboardKeyTransition.Down),
            AndroidKeyEventConstants.ActionUp => ProcessTransition(sample, KeyboardKeyTransition.Up),

            // ACTION_MULTIPLE with KEYCODE_UNKNOWN carries a string of characters that has no key
            // equivalent at all; the host passes the characters through as text.
            AndroidKeyEventConstants.ActionMultiple => ProcessRepeatedKey(sample),

            _ => false,
        };
    }

    /// <summary>
    /// Delivers a batch of characters that arrived without a corresponding key, which is what
    /// <c>ACTION_MULTIPLE</c> with <c>KEYCODE_UNKNOWN</c> represents.
    /// </summary>
    public bool ProcessCharacters(string characters, long eventTimeMilliseconds)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(characters) || !_options.ReceiveText)
            return false;

        _clock.Observe(eventTimeMilliseconds);

        if (!CanDeliverInput)
            return false;

        RaiseTextInput(new KeyboardTextEvent(
            NextEventHeader(AndroidUptimeInputClock.FromUptimeMilliseconds(eventTimeMilliseconds)),
            characters,
            IsSystemText: false,
            InputEventSource.Raw));
        return true;
    }

    /// <summary>
    /// Releases every key still held. The host calls this on focus loss, because Android does not
    /// deliver a key-up for a key that was down when the window went away.
    /// </summary>
    public bool ReleaseHeldKeys(string reason = "focus lost")
    {
        ThrowIfDisposed();

        if (_downKeys.Count == 0)
            return false;

        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.keyboard.release-held",
            new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["keys"] = _downKeys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

        int[] held = [.. _downKeys];
        _downKeys.Clear();

        foreach (int keyCode in held)
        {
            RaiseKeyChanged(new KeyboardKeyEvent(
                NextEventHeader(_clock.GetTimestamp()),
                AndroidKeyboardKeyMap.ToKeyboardKey(keyCode),
                KeyboardKeyTransition.Up,
                KeyboardModifierState.None,
                keyCode,
                0,
                0,
                AndroidKeyboardKeyMap.IsExtendedKey(keyCode),
                WasDown: true,
                AndroidKeyboardKeyMap.ToKeyLocation(keyCode),
                InputEventSource.Synthetic));
        }

        return true;
    }

    void IAndroidInputDevice.NotifyRemoved(InputFault fault)
    {
        ReleaseHeldKeys("device removed");
        MarkUnavailable(fault);
    }

    void IAndroidInputDevice.NotifyCaptureLost(string reason) => ReleaseHeldKeys(reason);

    protected override void Dispose(bool disposing)
    {
        _downKeys.Clear();
        base.Dispose(disposing);
    }

    private bool ProcessTransition(in AndroidKeyEventSample sample, KeyboardKeyTransition transition)
    {
        bool wasDown = _downKeys.Contains(sample.KeyCode);
        if (transition == KeyboardKeyTransition.Down)
            _downKeys.Add(sample.KeyCode);
        else
            _downKeys.Remove(sample.KeyCode);

        RaiseKeyChanged(new KeyboardKeyEvent(
            NextEventHeader(AndroidUptimeInputClock.FromUptimeMilliseconds(sample.EventTimeMilliseconds)),
            AndroidKeyboardKeyMap.ToKeyboardKey(sample.KeyCode),
            transition,
            AndroidModifierTranslation.FromMetaState(sample.MetaState),
            sample.KeyCode,
            sample.ScanCode,
            sample.RepeatCount + 1,
            AndroidKeyboardKeyMap.IsExtendedKey(sample.KeyCode),
            wasDown,
            AndroidKeyboardKeyMap.ToKeyLocation(sample.KeyCode),
            InputEventSource.Raw,
            sample.IsSystemKey));

        if (transition == KeyboardKeyTransition.Down)
            RaiseTextForKey(sample);

        return true;
    }

    private bool ProcessRepeatedKey(in AndroidKeyEventSample sample)
    {
        if (sample.KeyCode == AndroidKeyEventConstants.KeycodeUnknown)
            return false;

        // ACTION_MULTIPLE on a real key code means the key repeated while the window was not
        // reading events. Deliver it as a down/up pair per repeat so no consumer sees a key that
        // goes down without coming back up.
        int repeats = Math.Max(1, sample.RepeatCount);
        for (int index = 0; index < repeats; index++)
        {
            ProcessTransition(sample, KeyboardKeyTransition.Down);
            ProcessTransition(sample, KeyboardKeyTransition.Up);
        }

        return true;
    }

    private void RaiseTextForKey(in AndroidKeyEventSample sample)
    {
        if (!_options.ReceiveText || sample.UnicodeChar == 0)
            return;

        string? text = ToText(sample.UnicodeChar);
        if (text is null)
            return;

        RaiseTextInput(new KeyboardTextEvent(
            NextEventHeader(AndroidUptimeInputClock.FromUptimeMilliseconds(sample.EventTimeMilliseconds)),
            text,
            sample.IsSystemKey,
            InputEventSource.Raw));
    }

    /// <summary>
    /// Converts a <c>getUnicodeChar</c> result into text, filtering the control characters that
    /// have their own key events and would otherwise be inserted into a document as garbage.
    /// </summary>
    internal static string? ToText(int unicodeChar)
    {
        // The combining-accent flag marks a dead key whose character is not yet resolved. Dead-key
        // sequences belong to the IME path, so nothing is delivered here.
        const int CombiningAccentFlag = unchecked((int)0x80000000);
        if (unicodeChar == 0 || (unicodeChar & CombiningAccentFlag) != 0)
            return null;

        if (unicodeChar < 0x20 || unicodeChar == 0x7F)
            return null;

        if (!IsValidUnicodeScalar(unicodeChar))
            return null;

        return char.ConvertFromUtf32(unicodeChar);
    }

    private static bool IsValidUnicodeScalar(int value) =>
        value >= 0 && value <= 0x10FFFF && (value < 0xD800 || value > 0xDFFF);
}
