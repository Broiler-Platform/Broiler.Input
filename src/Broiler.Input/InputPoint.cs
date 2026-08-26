namespace Broiler.Input;

public readonly record struct InputPoint(double X, double Y, string CoordinateSpace)
{
    public static InputPoint ClientPixels(double x, double y) => new(x, y, "client-pixels");

    public static InputPoint ClientDeviceIndependentPixels(double x, double y) =>
        new(x, y, "client-dip");
}
