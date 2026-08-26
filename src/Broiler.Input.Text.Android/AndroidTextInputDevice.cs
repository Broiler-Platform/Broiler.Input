using System;
using System.Collections.Generic;
using Broiler.Input.Android;

namespace Broiler.Input.Text.Android;

/// <summary>
/// A <see cref="TextInputDevice"/> driven by an Android <c>InputConnection</c>.
/// </summary>
/// <remarks>
/// The host implements <c>View.onCreateInputConnection</c> and forwards each call here. This device
/// owns the composition state machine and decides which neutral event each call becomes.
///
/// The delivery rule matters, because a naive mapping makes editors insert text twice:
/// <list type="bullet">
/// <item><description>
/// While a composition is active, text is delivered <em>only</em> as
/// <see cref="TextCompositionEvent"/>. The terminating <c>commitText</c> or
/// <c>finishComposingText</c> raises <see cref="TextCompositionState.Committed"/> carrying the
/// final text, and no separate <see cref="TextInputEvent"/>.
/// </description></item>
/// <item><description>
/// With no composition active, <c>commitText</c> raises a <see cref="TextInputEvent"/> only.
/// </description></item>
/// </list>
/// So a consumer replaces the pre-edit span on every composition event and inserts on text events,
/// and exactly one of the two fires for any given piece of text.
/// </remarks>
public sealed class AndroidTextInputDevice : TextInputDevice, IAndroidInputDevice
{
    private readonly AndroidUptimeInputClock _clock;
    private readonly TextInputOpenOptions _options;
    private string _composingText = string.Empty;
    private bool _isComposing;

    public AndroidTextInputDevice(
        InputDeviceDescriptor descriptor,
        AndroidUptimeInputClock clock,
        TextInputOpenOptions? options = null,
        IAndroidEditorTextSource? editorTextSource = null)
        : base(descriptor, clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new TextInputOpenOptions();
        EditorTextSource = editorTextSource;
    }

    /// <summary>
    /// Raised when an input method asks for an editor mutation the neutral text contract cannot
    /// express.
    /// </summary>
    public event Action<AndroidTextEditRequest>? EditRequested;

    /// <summary>
    /// The editor the input method queries. Null until the host attaches one, in which case
    /// queries return empty results and an IME degrades to committed text only.
    /// </summary>
    public IAndroidEditorTextSource? EditorTextSource { get; set; }

    /// <summary>True while an input method is composing.</summary>
    public bool IsComposing => _isComposing;

    /// <summary>The text of the composition in flight, or an empty string when nothing is composing.</summary>
    public string ComposingText => _composingText;

    /// <summary>
    /// Backs <c>InputConnection.setComposingText</c>. Starts a composition on the first call and
    /// updates it afterwards. An empty string ends the composition without committing anything,
    /// which is how Android clears a pre-edit.
    /// </summary>
    public bool SetComposingText(string text, int newCursorPosition = 1, long eventTimeMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();

        Observe(eventTimeMilliseconds);

        if (!CanDeliverInput || !_options.ReportComposition)
            return false;

        if (text.Length == 0)
            return CancelComposition(eventTimeMilliseconds);

        TextCompositionState state = _isComposing ? TextCompositionState.Updated : TextCompositionState.Started;
        _isComposing = true;
        _composingText = text;

        int caret = ResolveCaret(text.Length, newCursorPosition);
        RaiseCompositionChanged(new TextCompositionEvent(
            NextEventHeader(Timestamp(eventTimeMilliseconds)),
            text,
            state,
            caret,
            0,
            InputEventSource.Raw));
        return true;
    }

    /// <summary>
    /// Backs <c>InputConnection.commitText</c>. Terminates a composition in flight, or delivers
    /// standalone text when nothing is composing.
    /// </summary>
    public bool CommitText(string text, int newCursorPosition = 1, long eventTimeMilliseconds = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfDisposed();

        Observe(eventTimeMilliseconds);

        if (!CanDeliverInput)
            return false;

        if (_isComposing)
        {
            _isComposing = false;
            _composingText = string.Empty;

            RaiseCompositionChanged(new TextCompositionEvent(
                NextEventHeader(Timestamp(eventTimeMilliseconds)),
                text,
                TextCompositionState.Committed,
                ResolveCaret(text.Length, newCursorPosition),
                0,
                InputEventSource.Raw));
            return true;
        }

        if (text.Length == 0)
            return false;

        RaiseTextInput(new TextInputEvent(
            NextEventHeader(Timestamp(eventTimeMilliseconds)),
            text,
            InputEventSource.Raw));
        return true;
    }

    /// <summary>
    /// Backs <c>InputConnection.finishComposingText</c>. The composing text stays in the document
    /// and stops being a pre-edit, so it is delivered as committed.
    /// </summary>
    public bool FinishComposingText(long eventTimeMilliseconds = 0)
    {
        ThrowIfDisposed();

        Observe(eventTimeMilliseconds);

        if (!CanDeliverInput || !_isComposing)
            return false;

        string committed = _composingText;
        _isComposing = false;
        _composingText = string.Empty;

        RaiseCompositionChanged(new TextCompositionEvent(
            NextEventHeader(Timestamp(eventTimeMilliseconds)),
            committed,
            TextCompositionState.Committed,
            committed.Length,
            0,
            InputEventSource.Raw));
        return true;
    }

    /// <summary>
    /// Ends a composition in flight without committing it — the editor lost focus, or the input
    /// method was dismissed.
    /// </summary>
    public bool CancelComposition(long eventTimeMilliseconds = 0)
    {
        ThrowIfDisposed();

        Observe(eventTimeMilliseconds);

        if (!CanDeliverInput || !_isComposing)
            return false;

        _isComposing = false;
        _composingText = string.Empty;

        RaiseCompositionChanged(new TextCompositionEvent(
            NextEventHeader(Timestamp(eventTimeMilliseconds)),
            string.Empty,
            TextCompositionState.Cancelled,
            0,
            0,
            InputEventSource.Raw));
        return true;
    }

    /// <summary>Backs <c>InputConnection.deleteSurroundingText</c>.</summary>
    public bool DeleteSurroundingText(int beforeLength, int afterLength)
    {
        ThrowIfDisposed();

        if (!CanDeliverInput || (beforeLength <= 0 && afterLength <= 0))
            return false;

        EditRequested?.Invoke(AndroidTextEditRequest.DeleteSurrounding(
            Math.Max(0, beforeLength),
            Math.Max(0, afterLength)));
        return true;
    }

    /// <summary>Backs <c>InputConnection.setSelection</c>.</summary>
    public bool SetSelection(int start, int end)
    {
        ThrowIfDisposed();

        if (!CanDeliverInput || start < 0 || end < 0)
            return false;

        EditRequested?.Invoke(AndroidTextEditRequest.Selection(start, end));
        return true;
    }

    /// <summary>
    /// Backs <c>InputConnection.setComposingRegion</c>, which marks existing text as composing
    /// without changing it.
    /// </summary>
    public bool SetComposingRegion(int start, int end)
    {
        ThrowIfDisposed();

        if (!CanDeliverInput || !_options.ReportComposition || start < 0 || end < start)
            return false;

        _isComposing = true;
        EditRequested?.Invoke(AndroidTextEditRequest.ComposingRegion(start, end));
        return true;
    }

    /// <summary>Backs <c>InputConnection.performEditorAction</c>.</summary>
    public bool PerformEditorAction(int actionCode)
    {
        ThrowIfDisposed();

        if (!CanDeliverInput)
            return false;

        EditRequested?.Invoke(AndroidTextEditRequest.EditorAction(actionCode));
        return true;
    }

    /// <summary>Backs <c>InputConnection.getTextBeforeCursor</c>.</summary>
    public string GetTextBeforeCursor(int maxLength) =>
        maxLength <= 0 ? string.Empty : EditorTextSource?.GetTextBeforeCursor(maxLength) ?? string.Empty;

    /// <summary>Backs <c>InputConnection.getTextAfterCursor</c>.</summary>
    public string GetTextAfterCursor(int maxLength) =>
        maxLength <= 0 ? string.Empty : EditorTextSource?.GetTextAfterCursor(maxLength) ?? string.Empty;

    /// <summary>Backs <c>InputConnection.getSelectedText</c>.</summary>
    public string GetSelectedText() => EditorTextSource?.GetSelectedText() ?? string.Empty;

    /// <summary>The current selection and composing region, as the input method sees it.</summary>
    public AndroidEditorSelection GetSelection() => EditorTextSource?.GetSelection() ?? default;

    /// <summary>
    /// The editor lost focus. Cancels a composition in flight when the open options ask for it.
    /// </summary>
    public bool NotifyFocusLost()
    {
        ThrowIfDisposed();

        if (!_options.CancelCompositionOnFocusLoss)
            return false;

        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.text.focus-lost",
            new Dictionary<string, string> { ["composing"] = _isComposing ? "true" : "false" });

        return CancelComposition();
    }

    void IAndroidInputDevice.NotifyRemoved(InputFault fault)
    {
        CancelComposition();
        MarkUnavailable(fault);
    }

    void IAndroidInputDevice.NotifyCaptureLost(string reason)
    {
        EmitDiagnostic(
            InputDiagnosticLevel.Information,
            "input.text.capture-lost",
            new Dictionary<string, string> { ["reason"] = reason });
        CancelComposition();
    }

    protected override void Dispose(bool disposing)
    {
        _isComposing = false;
        _composingText = string.Empty;
        base.Dispose(disposing);
    }

    private void Observe(long eventTimeMilliseconds)
    {
        if (eventTimeMilliseconds > 0)
            _clock.Observe(eventTimeMilliseconds);
    }

    private InputTimestamp Timestamp(long eventTimeMilliseconds) =>
        eventTimeMilliseconds > 0
            ? AndroidUptimeInputClock.FromUptimeMilliseconds(eventTimeMilliseconds)
            : _clock.GetTimestamp();

    /// <summary>
    /// Resolves Android's <c>newCursorPosition</c> into an offset inside the text.
    /// </summary>
    /// <remarks>
    /// Android defines it relative to the text just set: a value greater than zero counts from the
    /// end (1 places the caret immediately after the text), and a value of zero or less counts from
    /// the start (0 places it immediately before). The result is clamped into the text so a caret
    /// outside it cannot be reported as a composition offset.
    /// </remarks>
    internal static int ResolveCaret(int textLength, int newCursorPosition)
    {
        int caret = newCursorPosition > 0
            ? textLength + newCursorPosition - 1
            : newCursorPosition;

        return Math.Clamp(caret, 0, textLength);
    }
}
