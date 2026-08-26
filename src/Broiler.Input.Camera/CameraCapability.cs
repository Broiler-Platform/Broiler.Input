namespace Broiler.Input.Camera;

public sealed record CameraCapability(CameraFormat Format, string NativeSubtype, bool IsDefault = false);
