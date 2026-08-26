using System;

namespace Broiler.Input;

public sealed record InputDeliveryOptions
{
    public InputDeliveryOptions(int capacity, InputDeliveryOverflowPolicy overflowPolicy)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Delivery capacity must be positive.");

        Capacity = capacity;
        OverflowPolicy = overflowPolicy;
    }

    public int Capacity { get; }

    public InputDeliveryOverflowPolicy OverflowPolicy { get; }

    public static InputDeliveryOptions DiscreteDefault { get; } = new(64, InputDeliveryOverflowPolicy.DropOldest);
}
