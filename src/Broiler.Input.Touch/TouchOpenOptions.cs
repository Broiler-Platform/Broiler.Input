namespace Broiler.Input.Touch;

/// <summary>
/// Options for opening a <see cref="TouchInputDevice"/>.
/// </summary>
/// <param name="ReportPressure">
/// Deliver pressure with each contact. Devices without a pressure sensor report a nominal value, so
/// a consumer that treats pressure as meaningful should check the device capability rather than
/// this flag.
/// </param>
/// <param name="CancelContactsOnCaptureLoss">
/// Synthesize <see cref="TouchContactState.Cancelled"/> for every contact still down when the
/// device loses capture. Leaving it enabled is what keeps a backgrounded application from being
/// left with a contact that never lifts.
/// </param>
public sealed record TouchOpenOptions(
    bool ReportPressure = true,
    bool CancelContactsOnCaptureLoss = true);
