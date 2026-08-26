using System;

namespace Broiler.Input.Microphone;

public sealed record MicrophoneOpenOptions
{
    public MicrophoneOpenOptions(
        MicrophoneFormat? preferredFormat = null,
        MicrophoneSessionOptions? sessionOptions = null,
        MicrophoneEndpointRole role = MicrophoneEndpointRole.Console)
    {
        PreferredFormat = preferredFormat;
        SessionOptions = sessionOptions ?? MicrophoneSessionOptions.Default;
        Role = role;
    }

    public MicrophoneFormat? PreferredFormat { get; }

    public MicrophoneSessionOptions SessionOptions { get; }

    public MicrophoneEndpointRole Role { get; }

    public static MicrophoneOpenOptions Default { get; } = new();

    public static MicrophoneOpenOptions WithPreferredFormat(MicrophoneFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return new MicrophoneOpenOptions(format);
    }
}
