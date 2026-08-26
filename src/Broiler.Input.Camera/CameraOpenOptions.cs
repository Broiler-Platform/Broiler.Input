using System;

namespace Broiler.Input.Camera;

public sealed record CameraOpenOptions
{
    public CameraOpenOptions(
        CameraFormat? preferredFormat = null,
        CameraSessionOptions? sessionOptions = null)
    {
        PreferredFormat = preferredFormat;
        SessionOptions = sessionOptions ?? CameraSessionOptions.PreviewDefault;
    }

    public CameraFormat? PreferredFormat { get; }

    public CameraSessionOptions SessionOptions { get; }

    public static CameraOpenOptions Default { get; } = new();

    public static CameraOpenOptions WithPreferredFormat(CameraFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new CameraOpenOptions(format);
    }
}
