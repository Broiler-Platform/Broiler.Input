using System.Collections.Generic;

namespace Broiler.Input.Testing;

public sealed class RecordingInputDiagnosticSink : IInputDiagnosticSink
{
    private readonly List<InputDiagnosticEvent> _events = [];

    public IReadOnlyList<InputDiagnosticEvent> Events => _events;

    public void Write(InputDiagnosticEvent inputEvent)
    {
        _events.Add(inputEvent);
    }
}
