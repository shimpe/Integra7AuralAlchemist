using System;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.Controls;

/// <summary>One draggable control's undo-journal gesture: opened where a drag begins, closed where it
/// ends, so everything the drag changed is a single undo step however long the user took over it. See
/// <see cref="EditJournal.BeginGesture"/> for why the clock cannot work that out by itself -- a knob
/// records only when its snapped value changes, and a slow, careful drag is seconds between changes.
///
/// The scope has to outlive the handler that opened it, so a <c>using</c> is not available: this holds it
/// across the handlers instead and makes the pairing hard to get wrong. <see cref="End"/> may be called
/// any number of times, which is what lets a control end its drag from both pointer-released and
/// pointer-capture-lost without closing anything twice, and <see cref="Begin"/> closes a scope still held
/// from an earlier press, so no sequence of pointer events can leak one indefinitely.
///
/// UI thread only, like the pointer handlers that drive it.</summary>
/// <param name="journal">The journal to open gestures on. Defaults to the ambient one the application
/// records into; the parameter exists so a test can supply one with a controllable clock.</param>
public sealed class EditGesture(EditJournal? journal = null)
{
    private readonly EditJournal _journal = journal ?? EditJournal.Default;
    private IDisposable? _scope;

    public void Begin()
    {
        End();
        _scope = _journal.BeginGesture();
    }

    public void End()
    {
        var scope = _scope;
        _scope = null;
        scope?.Dispose();
    }
}
