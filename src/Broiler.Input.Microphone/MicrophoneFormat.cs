using System;

namespace Broiler.Input.Microphone;

public sealed record MicrophoneFormat
{
    public MicrophoneFormat(
        int sampleRate,
        int channelCount,
        int bitsPerSample,
        MicrophoneSampleFormat sampleFormat)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (channelCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(channelCount), "Channel count must be positive.");
        if (bitsPerSample <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Bits per sample must be positive.");

        SampleRate = sampleRate;
        ChannelCount = channelCount;
        BitsPerSample = bitsPerSample;
        SampleFormat = sampleFormat;
    }

    public int SampleRate { get; }

    public int ChannelCount { get; }

    public int BitsPerSample { get; }

    public MicrophoneSampleFormat SampleFormat { get; }

    public int BytesPerSample => checked((BitsPerSample + 7) / 8);

    public int BytesPerFrame => checked(BytesPerSample * ChannelCount);
}
