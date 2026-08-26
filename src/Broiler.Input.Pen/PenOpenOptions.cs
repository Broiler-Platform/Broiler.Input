namespace Broiler.Input.Pen;

/// <summary>
/// Options for opening a <see cref="PenInputDevice"/>.
/// </summary>
/// <param name="ReportHover">
/// Deliver <see cref="PenContactState.Hover"/> while the pen is detected above the surface but not
/// touching it.
/// </param>
/// <param name="ReportTilt">Deliver tilt angles when the device reports them.</param>
/// <param name="CancelContactsOnCaptureLoss">
/// Synthesize <see cref="PenContactState.Cancelled"/> when the device loses capture with the pen
/// down.
/// </param>
public sealed record PenOpenOptions(
    bool ReportHover = true,
    bool ReportTilt = true,
    bool CancelContactsOnCaptureLoss = true);
