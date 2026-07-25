using System;
using System.Collections.Generic;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Parameters the app shows but never offers an editor for.
///
/// The Setup block's Studio Set bank-select and program-change values report which Studio Set the
/// INTEGRA-7 currently holds. Writing them from the parameter grid would load a different Studio Set
/// on the device behind the app's back: nothing treats such an edit as a Studio Set change, so every
/// part would go on showing the outgoing set's parameters until something else forced a resync.
/// Reading them is useful, so they stay visible — just not editable.
///
/// Pure (no Avalonia) so it is unit-testable; the rendering side is
/// <c>DataTemplateProvider</c>, which turns a match into a plain readout.
/// </summary>
public static class ReadOnlyParameters
{
    private static readonly HashSet<string> Paths = new(StringComparer.Ordinal)
    {
        "Setup/Studio Set BS MSB",
        "Setup/Studio Set BS LSB",
        "Setup/Studio Set PC",
    };

    public static bool IsReadOnly(string path) => Paths.Contains(path);
}
