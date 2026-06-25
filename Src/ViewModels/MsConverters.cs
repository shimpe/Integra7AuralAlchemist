using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Integra7AuralAlchemist.ViewModels;

public static class MsConverters
{
    // Internal parts are blue; the external part is purple (visually distinct).
    public static readonly IValueConverter ExternalBrush =
        new FuncValueConverter<bool, IBrush>(isExt =>
            new SolidColorBrush(Color.Parse(isExt ? "#7a4fb0" : "#3d7eaa")));

    // Selected puck gets a white ring; others none.
    public static readonly IValueConverter SelectedThickness =
        new FuncValueConverter<bool, Thickness>(sel => sel ? new Thickness(3) : new Thickness(0));
}
