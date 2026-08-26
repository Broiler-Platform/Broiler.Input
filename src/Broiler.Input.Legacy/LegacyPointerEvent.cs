using Broiler.Input;
using Broiler.Input.Mouse;

namespace Broiler.Input.Legacy;

public readonly record struct LegacyPointerEvent(
    InputPoint Position,
    MouseButtons Buttons,
    MouseButton ChangedButton = MouseButton.None);
