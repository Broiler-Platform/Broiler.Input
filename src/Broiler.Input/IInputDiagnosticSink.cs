namespace Broiler.Input;

public interface IInputDiagnosticSink
{
    void Write(InputDiagnosticEvent inputEvent);
}

public sealed class NullInputDiagnosticSink : IInputDiagnosticSink
{
    public static NullInputDiagnosticSink Shared { get; } = new();

    private NullInputDiagnosticSink()
    {
    }

    public void Write(InputDiagnosticEvent inputEvent)
    {
    }
}
