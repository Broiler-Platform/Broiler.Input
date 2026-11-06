namespace Broiler.Input.Scanner;

public readonly record struct ScannerCaptureStatistics(long PagesCapturedCount, long PagesDeliveredCount,
    long PagesDroppedNewestCount, long PagesDroppedOldestCount, long FormatChangedCount, long BytesTransferred,
    int QueueDepth);
