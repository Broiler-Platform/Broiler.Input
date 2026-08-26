using System;

namespace Broiler.Input.Android;

/// <summary>
/// Converts Android's physical-pixel event coordinates into the device-independent pixels the rest
/// of Broiler works in.
/// </summary>
/// <remarks>
/// <c>MotionEvent</c> reports physical pixels, but <c>UiInputEvent</c> positions flow straight into
/// <c>Broiler.UI</c> hit-testing, which works in the same logical units as
/// <c>IUiHost.ViewportSize</c>. The desktop hosts already divide by the surface scale before
/// handing a position to Input, so Android does the same here rather than pushing the conversion
/// onto every consumer. <see cref="Density"/> is <c>DisplayMetrics.density</c>, which the host
/// updates on every configuration change.
/// </remarks>
public sealed class AndroidCoordinateSpace
{
    private double _density = 1.0;

    public AndroidCoordinateSpace(double density = 1.0)
    {
        Density = density;
    }

    /// <summary>
    /// <c>DisplayMetrics.density</c>. Values that are not finite and positive are rejected, because
    /// a zero or NaN density would silently move every contact to the origin.
    /// </summary>
    public double Density
    {
        get => _density;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Display density must be finite and positive.");

            _density = value;
        }
    }

    /// <summary>Converts a physical-pixel position into a device-independent <see cref="InputPoint"/>.</summary>
    public InputPoint ToInputPoint(double x, double y) =>
        InputPoint.ClientDeviceIndependentPixels(x / _density, y / _density);

    /// <summary>Converts a physical-pixel length into device-independent units.</summary>
    public double ToDeviceIndependent(double physicalPixels) => physicalPixels / _density;
}
