using System;

namespace Broiler.Input;

/// <summary>
/// Modifier keys held while an input event occurred.
/// </summary>
/// <remarks>
/// <para>
/// Modifier state is not a keyboard event's private business: a Ctrl-click, a
/// Shift-drag and a Ctrl-wheel are all chords that a pointer event has to carry, or
/// the consumer cannot tell them from the unmodified gesture. This lives in the root
/// <c>Broiler.Input</c> assembly because every device abstraction already depends on
/// it and none depend on each other — see ADR 0001. Putting it on the keyboard
/// package instead would make a mouse-only consumer take a dependency on keyboard.
/// </para>
/// <para>
/// The members and their values mirror
/// <c>Broiler.Input.Keyboard.KeyboardModifierState</c>, which stays where it is
/// because it is published API. <c>Broiler.Input.Contract.Tests</c> pins the two
/// layouts against each other so a member added to one and not the other fails.
/// </para>
/// </remarks>
[Flags]
public enum InputModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    LeftWindows = 8,
    RightWindows = 16,
    LeftShift = 32,
    RightShift = 64,
    LeftControl = 128,
    RightControl = 256,
    LeftAlt = 512,
    RightAlt = 1024,
}
