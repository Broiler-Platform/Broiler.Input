using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using Broiler.Input.Camera;
using Broiler.Input.Camera.Windows;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Windows;
using Broiler.Input.Legacy;
using Broiler.Input.Microphone;
using Broiler.Input.Microphone.Windows;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Windows;
using Broiler.Input.Testing;
using Broiler.Input.Windows;

namespace Broiler.Input.Contract.Tests;

internal static partial class Program
{
    private static void CoreHasNoWindowsReferences()
    {
        string[] references = [.. typeof(InputDevice).Assembly.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

        AssertFalse(references.Any(static reference => reference.Contains("Windows", StringComparison.OrdinalIgnoreCase)),
            "Broiler.Input must not reference Windows assemblies.");
    }

    private static void KeyboardMouseDoNotReferenceCameraOrMicrophone()
    {
        Assembly[] assemblies =
        [
            typeof(KeyboardInputDevice).Assembly,
            typeof(WindowsKeyboardInputDevice).Assembly,
            typeof(MouseInputDevice).Assembly,
            typeof(WindowsMouseInputDevice).Assembly,
        ];

        foreach (Assembly assembly in assemblies)
        {
            string[] references = [.. assembly.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];
            AssertFalse(references.Any(static reference =>
                    reference.Contains("Camera", StringComparison.Ordinal) ||
                    reference.Contains("Microphone", StringComparison.Ordinal)),
                $"{assembly.GetName().Name} must not reference Camera or Microphone assemblies.");
        }
    }

    private static void ProjectsOnlyReferenceNativePackages()
    {
        string componentRoot = FindComponentRoot();
        string[] projects = Directory.GetFiles(componentRoot, "*.csproj", SearchOption.AllDirectories);

        foreach (string project in projects)
        {
            XDocument document = XDocument.Load(project);
            foreach (XElement reference in document.Descendants("PackageReference"))
            {
                string? name = (string?)reference.Attribute("Include");
                AssertTrue(name is "Broiler.Native" or "Broiler.Native.Windows" or "Broiler.Native.Linux",
                    $"Only shared Native packages are allowed in {Path.GetRelativePath(componentRoot, project)}.");
                AssertTrue(((string?)reference.Attribute("Condition"))?.StartsWith("!Exists(", StringComparison.Ordinal) == true,
                    "Native packages must be a fallback when a source checkout is unavailable.");
            }
        }
    }

    /// <summary>
    /// <see cref="InputModifiers"/> and <c>KeyboardModifierState</c> are declared
    /// separately — the root cannot depend on the keyboard package, and the keyboard
    /// enum is published API that cannot move — so the UI layer casts between them.
    /// That cast is only safe while the two layouts agree, which this pins: a member
    /// added to one and not the other fails here rather than silently mistranslating
    /// a modifier at runtime.
    /// </summary>
    private static void PointerModifiersMirrorKeyboardModifiers()
    {
        string[] pointer = [.. Enum.GetNames<InputModifiers>().OrderBy(static name => name, StringComparer.Ordinal)];
        string[] keyboard = [.. Enum.GetNames<KeyboardModifierState>().OrderBy(static name => name, StringComparer.Ordinal)];

        if (!pointer.SequenceEqual(keyboard, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"InputModifiers and KeyboardModifierState must declare the same members. " +
                $"Only in InputModifiers: {string.Join(", ", pointer.Except(keyboard, StringComparer.Ordinal))}. " +
                $"Only in KeyboardModifierState: {string.Join(", ", keyboard.Except(pointer, StringComparer.Ordinal))}.");
        }

        foreach (string name in pointer)
        {
            int pointerValue = (int)Enum.Parse<InputModifiers>(name);
            int keyboardValue = (int)Enum.Parse<KeyboardModifierState>(name);
            AssertEqual(keyboardValue, pointerValue, $"InputModifiers.{name} must have the same value as KeyboardModifierState.{name}.");
        }
    }

    /// <summary>
    /// Every device abstraction depends on the root and on nothing else (ADR 0001).
    /// Modifier state on pointer events is the obvious temptation to break that, so
    /// the rule is asserted rather than trusted.
    /// </summary>
    private static void MouseDoesNotReferenceKeyboard()
    {
        string[] references = [.. typeof(MouseInputDevice).Assembly.GetReferencedAssemblies().Select(static reference => reference.Name ?? string.Empty)];

        AssertFalse(references.Any(static reference => reference.Contains("Keyboard", StringComparison.Ordinal)),
            "Broiler.Input.Mouse must not reference Broiler.Input.Keyboard; modifier state lives in the root.");
    }

    /// <summary>
    /// A pointer event carries the modifiers held with it, so a Ctrl-click can be told
    /// from a plain one. Defaulting to <see cref="InputModifiers.None"/> keeps every
    /// existing construction source-compatible.
    /// </summary>
    private static void PointerEventsCarryModifiers()
    {
        InputEventHeader header = new(InputDeviceId.FromOpaqueValue("mouse"), new InputTimestamp(1, TimeSpan.TicksPerSecond, "contract"), 1);
        InputPoint position = InputPoint.ClientDeviceIndependentPixels(4, 8);

        MouseButtonEvent button = new(header, position, MouseButtons.Left, MouseButton.Left, MouseButtonTransition.Down,
            Modifiers: InputModifiers.Control | InputModifiers.Shift);
        AssertEqual(InputModifiers.Control | InputModifiers.Shift, button.Modifiers, "Mouse button events carry modifiers.");

        MouseMoveEvent move = new(header, position, MouseButtons.None, Modifiers: InputModifiers.Alt);
        AssertEqual(InputModifiers.Alt, move.Modifiers, "Mouse move events carry modifiers.");

        MouseWheelEvent wheel = new(header, position, MouseButtons.None, MouseWheelAxis.Vertical, 1, Modifiers: InputModifiers.Control);
        AssertEqual(InputModifiers.Control, wheel.Modifiers, "Mouse wheel events carry modifiers.");

        MouseButtonEvent unmodified = new(header, position, MouseButtons.Left, MouseButton.Left, MouseButtonTransition.Down);
        AssertEqual(InputModifiers.None, unmodified.Modifiers, "Modifiers default to None.");
    }

    private static void PublicApiBaselineMatches()
    {
        string baselinePath = Path.Combine(AppContext.BaseDirectory, "api-baseline.txt");
        string[] expected = [.. File.ReadAllLines(baselinePath)
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .OrderBy(static line => line, StringComparer.Ordinal)];

        Assembly[] assemblies =
        [
            typeof(InputDevice).Assembly,
            typeof(CameraInputDevice).Assembly,
            typeof(WindowsCameraProvider).Assembly,
            typeof(WindowsInputMessage).Assembly,
            typeof(KeyboardInputDevice).Assembly,
            typeof(WindowsKeyboardInputDevice).Assembly,
            typeof(LegacyWindowInputAdapter).Assembly,
            typeof(MicrophoneInputDevice).Assembly,
            typeof(WindowsMicrophoneProvider).Assembly,
            typeof(MouseInputDevice).Assembly,
            typeof(WindowsMouseInputDevice).Assembly,
        ];

        string[] actual = [.. 
            assemblies.SelectMany(static assembly => 
            assembly.GetExportedTypes().Select(type => 
            $"{assembly.GetName().Name}:{type.FullName}")).OrderBy(static line => line, StringComparer.Ordinal)];

        if (!expected.SequenceEqual(actual))
        {
            string missing = string.Join(Environment.NewLine, expected.Except(actual, StringComparer.Ordinal));
            string added = string.Join(Environment.NewLine, actual.Except(expected, StringComparer.Ordinal));
            throw new InvalidOperationException($"API baseline mismatch. Missing:{Environment.NewLine}{missing}{Environment.NewLine}Added:{Environment.NewLine}{added}");
        }
    }
}
