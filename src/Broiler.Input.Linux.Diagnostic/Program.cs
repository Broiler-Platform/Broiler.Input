using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Input.Keyboard;
using Broiler.Input.Keyboard.Linux;
using Broiler.Input.Mouse;
using Broiler.Input.Mouse.Linux;

namespace Broiler.Input.Linux.Diagnostic;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CommandLineOptions commandLine = CommandLineOptions.Parse(args);
        if (commandLine.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        LinuxEvdevProviderOptions providerOptions = new(
            commandLine.InputDirectory,
            commandLine.SysfsInputRoot,
            commandLine.AcknowledgeRawInput,
            commandLine.PollTimeoutMilliseconds);

        PrintDependencies(commandLine.InputDirectory);
        IReadOnlyList<LinuxEvdevDeviceInfo> devices = LinuxEvdevDeviceDiscovery.DiscoverAll(providerOptions);
        PrintDevices(devices, commandLine.Kind);

        if (!commandLine.PrintEvents)
            return 0;

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("Event streaming requires Linux.");
            return 2;
        }

        if (!commandLine.AcknowledgeRawInput)
        {
            Console.Error.WriteLine("Event streaming reads raw/background evdev input. Re-run with --acknowledge-raw-input to opt in.");
            return 2;
        }

        return await StreamEventsAsync(commandLine, providerOptions).ConfigureAwait(false);
    }

    private static void PrintDependencies(string inputDirectory)
    {
        LinuxInputDependencyReport report = LinuxInputDependencies.CheckBaseline(inputDirectory);
        Console.WriteLine("Dependencies:");
        foreach (LinuxInputNativeLibraryStatus library in report.NativeLibraries)
            Console.WriteLine($"  {library.Id}: {(library.IsAvailable ? "available" : "missing")} - {library.Diagnostic}");

        Console.WriteLine($"  event-devices: {report.EventDevices.Diagnostic}");
        Console.WriteLine();
    }

    private static void PrintDevices(IReadOnlyList<LinuxEvdevDeviceInfo> devices, DiagnosticKind kind)
    {
        Console.WriteLine("Devices:");
        LinuxEvdevDeviceInfo[] matching = devices.Where(device => MatchesKind(kind, device.Kind)).ToArray();
        Console.WriteLine($"  summary: total={matching.Length}, available={matching.Count(static device => device.Descriptor.Availability == InputDeviceAvailability.Available)}, permission-denied={matching.Count(static device => device.Descriptor.Availability == InputDeviceAvailability.PermissionDenied)}");
        foreach (LinuxEvdevDeviceInfo device in matching)
        {
            Console.WriteLine($"  {device.Kind}: {device.DisplayName}");
            Console.WriteLine($"    id: {device.Descriptor.Id}");
            Console.WriteLine($"    event: {device.EventName}");
            Console.WriteLine($"    availability: {device.Descriptor.Availability}");
        }

        if (matching.Length == 0)
            Console.WriteLine("  none");

        Console.WriteLine();
    }

    private static async Task<int> StreamEventsAsync(CommandLineOptions commandLine, LinuxEvdevProviderOptions providerOptions)
    {
        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        List<InputDevice> opened = [];
        try
        {
            if (commandLine.Kind is DiagnosticKind.All or DiagnosticKind.Keyboard)
                await OpenKeyboardsAsync(providerOptions, opened, cancellation.Token).ConfigureAwait(false);

            if (commandLine.Kind is DiagnosticKind.All or DiagnosticKind.Mouse)
                await OpenMiceAsync(providerOptions, opened, cancellation.Token).ConfigureAwait(false);

            if (opened.Count == 0)
            {
                Console.WriteLine("No matching readable evdev devices were opened.");
                return 1;
            }

            Console.WriteLine("Streaming sanitized event summaries. Press Ctrl+C to stop.");
            await Task.Delay(commandLine.DurationMilliseconds, cancellation.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (LinuxInputException exception)
        {
            Console.Error.WriteLine($"{exception.Fault.Category}: {exception.Message}");
            return 3;
        }
        finally
        {
            foreach (InputDevice device in opened)
            {
                await device.StopAsync().ConfigureAwait(false);
                await device.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task OpenKeyboardsAsync(
        LinuxEvdevProviderOptions providerOptions,
        List<InputDevice> opened,
        CancellationToken cancellationToken)
    {
        LinuxKeyboardProvider provider = new(providerOptions);
        IReadOnlyList<InputDeviceDescriptor> descriptors = await provider.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        foreach (InputDeviceDescriptor descriptor in descriptors.Where(static descriptor => descriptor.Availability == InputDeviceAvailability.Available))
        {
            KeyboardInputDevice keyboard = await provider.OpenAsync(descriptor, new KeyboardOpenOptions(ReceiveText: false), cancellationToken).ConfigureAwait(false);
            keyboard.KeyChanged += inputEvent =>
                Console.WriteLine($"keyboard {descriptor.DisplayName}: {inputEvent.Key} {inputEvent.Transition} code={inputEvent.NativeKeyCode} mods={inputEvent.Modifiers}");
            await keyboard.StartAsync(cancellationToken).ConfigureAwait(false);
            opened.Add(keyboard);
        }
    }

    private static async Task OpenMiceAsync(
        LinuxEvdevProviderOptions providerOptions,
        List<InputDevice> opened,
        CancellationToken cancellationToken)
    {
        LinuxMouseProvider provider = new(providerOptions);
        IReadOnlyList<InputDeviceDescriptor> descriptors = await provider.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        foreach (InputDeviceDescriptor descriptor in descriptors.Where(static descriptor => descriptor.Availability == InputDeviceAvailability.Available))
        {
            MouseInputDevice mouse = await provider.OpenAsync(descriptor, new MouseOpenOptions(), cancellationToken).ConfigureAwait(false);
            mouse.Moved += inputEvent =>
                Console.WriteLine($"mouse {descriptor.DisplayName}: move dx={inputEvent.Position.X} dy={inputEvent.Position.Y} buttons={inputEvent.Buttons}");
            mouse.ButtonChanged += inputEvent =>
                Console.WriteLine($"mouse {descriptor.DisplayName}: button {inputEvent.Button} {inputEvent.Transition} buttons={inputEvent.Buttons}");
            mouse.WheelChanged += inputEvent =>
                Console.WriteLine($"mouse {descriptor.DisplayName}: wheel {inputEvent.Axis} delta={inputEvent.DeltaNotches} buttons={inputEvent.Buttons}");
            await mouse.StartAsync(cancellationToken).ConfigureAwait(false);
            opened.Add(mouse);
        }
    }

    private static bool MatchesKind(DiagnosticKind requested, LinuxEvdevDeviceKind actual) =>
        requested == DiagnosticKind.All ||
        (requested == DiagnosticKind.Keyboard && actual == LinuxEvdevDeviceKind.Keyboard) ||
        (requested == DiagnosticKind.Mouse && actual == LinuxEvdevDeviceKind.Mouse);

    private static void PrintHelp()
    {
        Console.WriteLine("Broiler.Input.Linux.Diagnostic");
        Console.WriteLine("  --kind all|keyboard|mouse");
        Console.WriteLine("  --input-dir /dev/input");
        Console.WriteLine("  --sysfs-root /sys/class/input");
        Console.WriteLine("  --events");
        Console.WriteLine("  --acknowledge-raw-input");
        Console.WriteLine("  --duration-ms 10000");
    }

    private enum DiagnosticKind
    {
        All = 0,
        Keyboard,
        Mouse,
    }

    private sealed record CommandLineOptions(
        DiagnosticKind Kind,
        string InputDirectory,
        string SysfsInputRoot,
        bool PrintEvents,
        bool AcknowledgeRawInput,
        int DurationMilliseconds,
        int PollTimeoutMilliseconds,
        bool ShowHelp)
    {
        public static CommandLineOptions Parse(string[] args)
        {
            DiagnosticKind kind = DiagnosticKind.All;
            string inputDirectory = LinuxEventDeviceAccessProbe.DefaultInputDirectory;
            string sysfsInputRoot = "/sys/class/input";
            bool printEvents = false;
            bool acknowledgeRawInput = false;
            int durationMilliseconds = 10_000;
            int pollTimeoutMilliseconds = 50;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                    case "--kind":
                        kind = ParseKind(ReadValue(args, ref i, arg));
                        break;
                    case "--input-dir":
                        inputDirectory = ReadValue(args, ref i, arg);
                        break;
                    case "--sysfs-root":
                        sysfsInputRoot = ReadValue(args, ref i, arg);
                        break;
                    case "--events":
                        printEvents = true;
                        break;
                    case "--acknowledge-raw-input":
                        acknowledgeRawInput = true;
                        break;
                    case "--duration-ms":
                        durationMilliseconds = ParsePositiveInt(ReadValue(args, ref i, arg), arg);
                        break;
                    case "--poll-ms":
                        pollTimeoutMilliseconds = ParsePositiveInt(ReadValue(args, ref i, arg), arg);
                        break;
                    default:
                        throw new ArgumentException("Unknown argument: " + arg);
                }
            }

            return new CommandLineOptions(
                kind,
                inputDirectory,
                sysfsInputRoot,
                printEvents,
                acknowledgeRawInput,
                durationMilliseconds,
                pollTimeoutMilliseconds,
                showHelp);
        }

        private static DiagnosticKind ParseKind(string value) =>
            value.ToLowerInvariant() switch
            {
                "all" => DiagnosticKind.All,
                "keyboard" => DiagnosticKind.Keyboard,
                "mouse" => DiagnosticKind.Mouse,
                _ => throw new ArgumentException("Unsupported kind: " + value),
            };

        private static string ReadValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException(optionName + " requires a value.");

            index++;
            return args[index];
        }

        private static int ParsePositiveInt(string value, string optionName)
        {
            if (!int.TryParse(value, out int result) || result <= 0)
                throw new ArgumentException(optionName + " requires a positive integer.");

            return result;
        }
    }
}
