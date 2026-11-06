namespace Broiler.Input.Scanner;

public readonly record struct ScannerCaptureStateChangedEvent(ScannerCaptureState PreviousState,
    ScannerCaptureState CurrentState, InputTimestamp Timestamp, InputFault? Fault = null,
    ScannerFaultCode FaultCode = ScannerFaultCode.None);
