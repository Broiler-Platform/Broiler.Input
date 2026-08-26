namespace Broiler.Input.Microphone;

public enum MicrophoneCaptureState
{
    Stopped = 0,
    Starting,
    Running,
    Stopping,
    Faulted,
    Invalidated,
}
