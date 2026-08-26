namespace Broiler.Input.Camera;

public enum CameraCaptureState
{
    Stopped = 0,
    Starting,
    Running,
    Stopping,
    Faulted,
    Invalidated,
}
