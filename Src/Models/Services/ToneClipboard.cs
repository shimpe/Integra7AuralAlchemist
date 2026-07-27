using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>One tone, copied from a part and waiting to be pasted into another.
///
/// A whole <see cref="Integra7Snapshot"/> rather than a bag of values, because that is exactly what
/// <c>StudioSetSnapshotService.RestoreToneAsync</c> takes, and because the snapshot already names its own
/// engine -- which is what lets the paste be refused when the target part holds a different one.
///
/// Not persisted, and not static: an instance held by <c>MainWindowViewModel</c> for the life of the
/// window. A clipboard that outlived the process would be a surprise, and the library is where a tone
/// goes when it is meant to be kept.</summary>
public sealed class ToneClipboard
{
    public Integra7Snapshot? Content { get; private set; }

    public bool HasContent => Content is not null;

    /// <summary>Raised when the contents change, so the Paste button can enable itself. Fired from
    /// whichever thread called <see cref="Put"/> -- a UI listener marshals back itself, as it does for
    /// <c>EditJournal.Changed</c>.</summary>
    public event Action? Changed;

    public void Put(Integra7Snapshot snapshot)
    {
        Content = snapshot;
        Changed?.Invoke();
    }
}
