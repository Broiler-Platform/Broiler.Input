using Broiler.Input;

namespace Broiler.Input.Camera;

public readonly record struct CameraFrameReadyEvent(InputEventHeader Header, CameraFrameLease Frame);
