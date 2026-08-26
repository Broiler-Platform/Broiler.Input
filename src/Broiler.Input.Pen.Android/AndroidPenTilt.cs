using System;

namespace Broiler.Input.Pen.Android;

/// <summary>
/// Converts Android's polar stylus orientation into the Cartesian tilt pair Broiler reports.
/// </summary>
/// <remarks>
/// Android describes stylus attitude with two axes: <c>AXIS_TILT</c>, the angle away from the
/// surface normal, and <c>AXIS_ORIENTATION</c>, the compass direction the tip points, where 0 is
/// away from the user and positive rotates clockwise. <see cref="PenContactEvent"/> instead carries
/// <c>TiltX</c>/<c>TiltY</c>, the plane angles used by W3C Pointer Events: <c>TiltX</c> positive
/// toward the right of the screen and <c>TiltY</c> positive toward the user.
///
/// The conversion is the standard one, but its sign convention has not been checked against a real
/// digitizer — that is part of the pen half of the phase A2 exit gate, which requires hardware.
/// Both axes are reported in degrees.
/// </remarks>
public static class AndroidPenTilt
{
    /// <summary>Converts (tilt, orientation) in radians into (tiltX, tiltY) in degrees.</summary>
    public static (double TiltX, double TiltY) ToDegrees(double tiltRadians, double orientationRadians)
    {
        if (!double.IsFinite(tiltRadians) || !double.IsFinite(orientationRadians) || tiltRadians == 0)
            return (0, 0);

        double sinTilt = Math.Sin(tiltRadians);
        double cosTilt = Math.Cos(tiltRadians);

        double tiltXRadians = Math.Atan2(sinTilt * Math.Sin(orientationRadians), cosTilt);

        // Negated because Android measures orientation 0 as pointing away from the user while
        // Pointer Events treats positive TiltY as tilting toward the user.
        double tiltYRadians = Math.Atan2(-sinTilt * Math.Cos(orientationRadians), cosTilt);

        return (RadiansToDegrees(tiltXRadians), RadiansToDegrees(tiltYRadians));
    }

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
