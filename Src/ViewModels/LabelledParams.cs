namespace Integra7AuralAlchemist.ViewModels;

// Tiny carriers for the friendly editors' uniform lists — a row of receive switches, twelve scale-tune
// knobs, six output-assign combos. They only pair a label with an already-reactive parameter wrapper,
// so they are plain classes: deriving from ViewModelBase would make the ViewLocator hunt for a View.

/// <summary>One labelled On/Off switch.</summary>
public sealed class LabelledSwitch
{
    public LabelledSwitch(string label, ParamBool param)
    {
        Label = label;
        Param = param;
    }

    public string Label { get; }
    public ParamBool Param { get; }
}

/// <summary>One labelled numeric knob.</summary>
public sealed class LabelledNumber
{
    public LabelledNumber(string label, ParamInt param)
    {
        Label = label;
        Param = param;
    }

    public string Label { get; }
    public ParamInt Param { get; }
}

/// <summary>One labelled combo box over an enum parameter.</summary>
public sealed class LabelledChoice
{
    public LabelledChoice(string label, ParamString param)
    {
        Label = label;
        Param = param;
    }

    public string Label { get; }
    public ParamString Param { get; }
}
