using System;

namespace Broiler.Input.Scanner.Windows;

public sealed class InputScannerException(InputFault fault, ScannerFaultCode faultCode = ScannerFaultCode.None)
    : InvalidOperationException(fault?.Message, fault?.Exception)
{
    public InputFault Fault { get; } = fault ?? throw new ArgumentNullException(nameof(fault));

    public ScannerFaultCode FaultCode { get; } = faultCode;
}
