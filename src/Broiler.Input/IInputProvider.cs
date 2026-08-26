using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input;

public interface IInputProvider<TDevice, in TOptions>
    where TDevice : InputDevice
{
    ValueTask<IReadOnlyList<InputDeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default);

    ValueTask<TDevice> OpenAsync(
        InputDeviceDescriptor descriptor,
        TOptions options,
        CancellationToken cancellationToken = default);
}
