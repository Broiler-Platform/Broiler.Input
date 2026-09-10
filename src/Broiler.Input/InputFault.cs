using System;

namespace Broiler.Input;

public sealed class InputFault(InputErrorCategory category, string message, Exception? exception = null,
    int? nativeErrorCode = null, string? nativeFacility = null)
{
    public InputErrorCategory Category { get; } = category;

    public string Message { get; } = string.IsNullOrWhiteSpace(message) ? category.ToString() : message;

    public Exception? Exception { get; } = exception;

    public int? NativeErrorCode { get; } = nativeErrorCode;

    public string? NativeFacility { get; } = nativeFacility;

    public override string ToString() => Message;
}
