namespace Broiler.Input.Mouse.Windows;

public sealed record WindowsMouseMessageOptions(
    double CoordinateScale = 1.0,
    string CoordinateSpace = "client-pixels",
    bool ConvertWheelScreenPointToClient = true);
