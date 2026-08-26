using System;
using Broiler.Input;

namespace Broiler.Input.Microphone;

public sealed record MicrophoneSessionOptions
{
    public MicrophoneSessionOptions(
        InputDeliveryOptions? deliveryOptions = null,
        bool reportSilence = true,
        bool reportDiscontinuities = true,
        TimeSpan? requestedLatency = null)
    {
        DeliveryOptions = deliveryOptions ?? new InputDeliveryOptions(8, InputDeliveryOverflowPolicy.DropOldest);
        ReportSilence = reportSilence;
        ReportDiscontinuities = reportDiscontinuities;
        RequestedLatency = requestedLatency ?? TimeSpan.FromMilliseconds(100);

        if (RequestedLatency < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(requestedLatency), "Requested latency must not be negative.");
    }

    public InputDeliveryOptions DeliveryOptions { get; }

    public bool ReportSilence { get; }

    public bool ReportDiscontinuities { get; }

    public TimeSpan RequestedLatency { get; }

    public static MicrophoneSessionOptions Default { get; } = new();
}
