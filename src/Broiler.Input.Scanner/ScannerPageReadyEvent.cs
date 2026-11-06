namespace Broiler.Input.Scanner;

public readonly record struct ScannerPageReadyEvent(InputEventHeader Header, ScannerPageLease Page);
