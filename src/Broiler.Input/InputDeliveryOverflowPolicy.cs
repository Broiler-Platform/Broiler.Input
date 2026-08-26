namespace Broiler.Input;

public enum InputDeliveryOverflowPolicy
{
    DropNewest = 0,
    DropOldest,
    KeepLatest,
    Fail,
}
