namespace Broiler.Input.Testing;

public sealed record FakeInputOpenOptions(InputDeliveryOptions? DeliveryOptions = null)
{
    public InputDeliveryOptions EffectiveDeliveryOptions => DeliveryOptions ?? InputDeliveryOptions.DiscreteDefault;
}
