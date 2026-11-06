using System;

namespace Broiler.Input.Scanner.Linux;

public sealed class InputScannerException : InvalidOperationException
{
    public InputScannerException(InputFault fault, ScannerFaultCode faultCode = ScannerFaultCode.None)
        : base(fault?.Message ?? throw new ArgumentNullException(nameof(fault)), fault.Exception)
    {
        Fault = fault;
        FaultCode = faultCode;
    }

    public InputFault Fault { get; }

    public ScannerFaultCode FaultCode { get; }
}
