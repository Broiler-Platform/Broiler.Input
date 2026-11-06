using System;

namespace Broiler.Input.Scanner;

public readonly record struct ScannerProgressEvent
{
    public ScannerProgressEvent(InputEventHeader header, long pageNumber, double progressFraction, long bytesTransferred)
    {
        if (pageNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must not be negative.");

        if (progressFraction is < 0.0 or > 1.0 || double.IsNaN(progressFraction))
            throw new ArgumentOutOfRangeException(nameof(progressFraction), "Progress fraction must be in [0.0, 1.0].");

        if (bytesTransferred < 0)
            throw new ArgumentOutOfRangeException(nameof(bytesTransferred), "Transferred byte count must not be negative.");

        Header = header;
        PageNumber = pageNumber;
        ProgressFraction = progressFraction;
        BytesTransferred = bytesTransferred;
    }

    public InputEventHeader Header { get; }

    public long PageNumber { get; }

    public double ProgressFraction { get; }

    public long BytesTransferred { get; }
}
