using Broiler.Input;
using Broiler.Input.Mouse;

namespace Broiler.Input.Legacy;

public readonly record struct LegacyMouseWheelEvent(
    InputPoint Position,
    double DeltaNotches,
    MouseButtons Buttons,
    MouseWheelAxis Axis = MouseWheelAxis.Vertical);
