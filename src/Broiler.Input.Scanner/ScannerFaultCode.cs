namespace Broiler.Input.Scanner;

public enum ScannerFaultCode
{
    None = 0,
    PaperJam,
    FeederEmpty,
    CoverOpen,
    LampFailure,
    MultiFeedDetected,
    TransferAborted,
}
