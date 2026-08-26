using System.Runtime.InteropServices;

namespace Broiler.Input.Keyboard.Windows;

internal static partial class WindowsKeyboardNativeMethods
{
    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int virtualKey);
}
