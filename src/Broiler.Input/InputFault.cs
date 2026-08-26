using System;

namespace Broiler.Input;

public sealed class InputFault
{
    public InputFault(
        InputErrorCategory category,
        string message,
        Exception? exception = null,
        int? nativeErrorCode = null,
        string? nativeFacility = null)
    {
        Category = category;
        Message = string.IsNullOrWhiteSpace(message) ? category.ToString() : message;
        Exception = exception;
        NativeErrorCode = nativeErrorCode;
        NativeFacility = nativeFacility;
    }

    public InputErrorCategory Category { get; }

    public string Message { get; }

    public Exception? Exception { get; }

    public int? NativeErrorCode { get; }

    public string? NativeFacility { get; }

    public override string ToString() => Message;
}
