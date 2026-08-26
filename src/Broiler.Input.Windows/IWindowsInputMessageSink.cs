namespace Broiler.Input.Windows;

public interface IWindowsInputMessageSink
{
    bool ProcessMessage(in WindowsInputMessage message);
}
