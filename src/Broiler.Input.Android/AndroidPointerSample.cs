namespace Broiler.Input.Android;

/// <summary>
/// One pointer's data inside a <see cref="AndroidMotionSample"/>, mirroring the per-pointer-index
/// accessors on <c>MotionEvent</c>.
/// </summary>
/// <param name="PointerId">
/// <c>MotionEvent.getPointerId(index)</c>. Stable for the lifetime of a contact, which is what
/// makes multi-touch tracking possible; the pointer <em>index</em> is not stable and must not be
/// used as an identity.
/// </param>
/// <param name="X">Horizontal position in physical pixels, relative to the receiving view.</param>
/// <param name="Y">Vertical position in physical pixels, relative to the receiving view.</param>
/// <param name="Pressure">
/// <c>MotionEvent.getPressure(index)</c>. Nominally 0..1, but Android documents that some devices
/// report above 1; translation clamps it.
/// </param>
/// <param name="ToolType">One of the <c>ToolType*</c> values on <see cref="AndroidMotionEventConstants"/>.</param>
/// <param name="OrientationRadians">
/// <c>AXIS_ORIENTATION</c>. For a stylus, the direction the tip points, where 0 is away from the
/// user.
/// </param>
/// <param name="TiltRadians">
/// <c>AXIS_TILT</c>. The angle between the stylus and the surface normal, 0 when perpendicular.
/// </param>
public readonly record struct AndroidPointerSample(
    int PointerId,
    float X,
    float Y,
    float Pressure = 0f,
    int ToolType = AndroidMotionEventConstants.ToolTypeFinger,
    float OrientationRadians = 0f,
    float TiltRadians = 0f);
