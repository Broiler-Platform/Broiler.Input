namespace Broiler.Input.Scanner;

public enum ScannerCaptureState
{
    Stopped = 0,
    Starting,
    WarmingUp,
    Scanning,
    Stopping,
    Completed,
    Faulted,
    Invalidated,
}
