namespace Broiler.Input.Keyboard.Android;

/// <summary>
/// A neutral snapshot of one <c>android.view.KeyEvent</c>.
/// </summary>
/// <param name="Action">
/// <c>KeyEvent.getAction()</c>: <c>ACTION_DOWN</c>, <c>ACTION_UP</c>, or <c>ACTION_MULTIPLE</c>.
/// </param>
/// <param name="KeyCode">
/// <c>KeyEvent.getKeyCode()</c>. Note that <c>KEYCODE_DEL</c> is backspace and
/// <c>KEYCODE_FORWARD_DEL</c> is the Delete key.
/// </param>
/// <param name="ScanCode">
/// <c>KeyEvent.getScanCode()</c>. Zero for virtual keys, which is every key a soft keyboard sends.
/// </param>
/// <param name="MetaState">
/// <c>KeyEvent.getMetaState()</c>, the modifier and lock bitfield.
/// </param>
/// <param name="RepeatCount">
/// <c>KeyEvent.getRepeatCount()</c>. Zero for the initial press, then non-zero while auto-repeat
/// runs.
/// </param>
/// <param name="UnicodeChar">
/// <c>KeyEvent.getUnicodeChar(metaState)</c>, or 0 when the key produces no character. The host
/// resolves this because the mapping depends on the active layout, which only Android knows.
/// </param>
/// <param name="EventTimeMilliseconds">
/// <c>KeyEvent.getEventTime()</c>, in <c>SystemClock.uptimeMillis()</c> units.
/// </param>
/// <param name="Source"><c>KeyEvent.getSource()</c>.</param>
/// <param name="IsSystemKey">
/// <c>KeyEvent.isSystem()</c>. Set for keys the platform owns — Back, Home, volume — which a host
/// must not silently swallow.
/// </param>
public readonly record struct AndroidKeyEventSample(
    int Action,
    int KeyCode,
    int ScanCode = 0,
    int MetaState = 0,
    int RepeatCount = 0,
    int UnicodeChar = 0,
    long EventTimeMilliseconds = 0,
    int Source = Broiler.Input.Android.AndroidInputSource.Keyboard,
    bool IsSystemKey = false);
