using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using Integra7AuralAlchemist.Models.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Integra7AuralAlchemist.ViewModels;

/// <summary>Which patch-list format to write, as a dialog.
///
/// <b>It picks a format and closes, and that is the whole of it.</b> No file is named here and nothing is
/// written: where the file goes is the operating system's own save dialog's question, and asking both in one
/// window would mean either building a second file browser or asking for a path before the extension is
/// known. So this is one question, and the save dialog that follows is told which extension to filter by.
///
/// <b>The list is <see cref="PatchListWriters.All"/>, not a copy of it.</b> A format added there appears here
/// with nothing else to change, which is the arrangement that keeps the picker from ever offering a format
/// the export cannot write.
///
/// Both commands answer a writer or null and both close the window with it -- the shape
/// <see cref="ConfirmViewModel"/> and <see cref="TonePickerViewModel"/> already use, and the reason the
/// caller can read the answer without a second flag to keep in step.</summary>
public sealed partial class PatchListExportViewModel : ViewModelBase
{
    public PatchListExportViewModel()
    {
        // Parameterless, for the reason ConfirmViewModel gives: a ReactiveCommand<Unit, T> invoked from a
        // button with no CommandParameter is handed null, and casting null to Unit throws.
        //
        // Guarded on there being a selection, because a ListBox can be emptied of one -- clicking the space
        // below the rows does it -- and Export would then answer null, which is this dialog's word for
        // "cancelled". A disabled button says what a silent no-op cannot.
        ExportCommand = ReactiveCommand.Create(() => Selected,
            this.WhenAnyValue(x => x.Selected).Select(selected => selected is not null));
        CancelCommand = ReactiveCommand.Create(() => (IPatchListWriter?)null);
    }

    public IReadOnlyList<IPatchListWriter> Formats => PatchListWriters.All;

    /// <summary>Starts on the first format, which is Reaper -- see <see cref="PatchListWriters.All"/> for why
    /// it is first. A picker that opened with nothing selected would show a disabled Export button as the
    /// first thing the user saw.</summary>
    [Reactive] private IPatchListWriter? _selected = PatchListWriters.All[0];

    // Qualified: ReactiveUI 24 ships two ReactiveCommand<,> types, the core's over RxVoid and this one over
    // System.Reactive's Unit. The csproj aliases the bare name for the factory, but an alias cannot name an
    // open generic, so the declarations say which they mean.
    public ReactiveUI.Reactive.ReactiveCommand<Unit, IPatchListWriter?> ExportCommand { get; }
    public ReactiveUI.Reactive.ReactiveCommand<Unit, IPatchListWriter?> CancelCommand { get; }
}
