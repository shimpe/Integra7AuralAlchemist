using System.ComponentModel;
using CoreMidi;

namespace Integra7AuralAlchemist.Models.Data;

internal enum EnumToneType
{
    SuperNaturalAcoustic,
    SuperNaturalSynth,
    SuperNaturalDrums,
    PCMSynth,
    PCMDrums
}

internal enum EnumToneBank
{
    Preset,
    GeneralMidi,
    ExSN1,
    ExSN2,
    ExSN3,
    ExSN4,
    ExSN5,
    ExSN6,
    SRX01,
    SRX02,
    SRX03,
    SRX04,
    SRX05,
    SRX06,
    SRX07,
    SRX08,
    SRX09,
    SRX10,
    SRX11,
    SRX12,
    ExPCM
}

internal enum EnumCategory
{
    AcPiano,
    OtherKeyboards,
    Organ,
    EGuitar,
    DistGuitar,
    AcBass,
    EBass,
    AcGuitar,
    AccordionHarmonica,
    BellMallet,
    Percussion,
    PluckedStroke,
    Strings,
    VoxChoir,
    Brass,
    Sax,
    Wind,
    Flute,
    SynthPadStrings,
    Pulsating,
    SynthBrass,
    SynthPolyKey,
    SynthBellPad,
    SynthSeqPop,
    SynthBass,
    SynthLead,
    FX,
    EPiano,
    Hit,
    Drums,
    BeatGroove,
    Recorder,
    SoundFX,
    Phrase
}

internal enum EnumInternalUserDefined
{
    Internal,
    UserDefined
}

/// <summary>
/// One selectable patch. A class implementing <see cref="INotifyPropertyChanged"/> rather than a
/// record, for two reasons that go together.
///
/// It has an identity (<see cref="Id"/>) and a mutable <see cref="Name"/>: storing a tone to a user
/// slot renames that slot, and every part's preset grid binds the same instance -- the whole preset
/// list is handed to all 17 part view models by reference -- so the rename has to be announced or the
/// grids keep showing the old name while the instrument shows the new one.
///
/// A record cannot announce it cleanly: the synthesized equality compares every instance field, and an
/// event's backing field is one, so two presets would stop being equal purely because one had a bound
/// grid. Value equality bought nothing here anyway -- Ids are unique, so it agreed with reference
/// equality at the one place presets are compared.
/// </summary>
public class Integra7Preset : INotifyPropertyChanged
{
    public Integra7Preset(int Id, string InternalUserDefined, string ToneType, string ToneBank, int Number, string Name,
        int MSB, int LSB, int PC, string Category)
    {
        _id = Id;
        _toneTypeStr = ToneType;
        _toneBankStr = ToneBank;
        _number = Number;
        _name = Name;
        _msb = MSB;
        _lsb = LSB;
        _pc = PC;
        _categoryStr = Category;
        _toneType = ToneType switch
        {
            "SN-A" => EnumToneType.SuperNaturalAcoustic,
            "SN-S" => EnumToneType.SuperNaturalSynth,
            "SN-D" => EnumToneType.SuperNaturalDrums,
            "PCMS" => EnumToneType.PCMSynth,
            "PCMD" => EnumToneType.PCMDrums,
            _ => throw new MidiException("Invalid string value for tone type: " + ToneType)
        };
        _toneBank = ToneBank switch
        {
            "PRST" => EnumToneBank.Preset,
            "GM2/GM2#" => EnumToneBank.GeneralMidi,
            "ExSN1" => EnumToneBank.ExSN1,
            "ExSN2" => EnumToneBank.ExSN2,
            "ExSN3" => EnumToneBank.ExSN3,
            "ExSN4" => EnumToneBank.ExSN4,
            "ExSN5" => EnumToneBank.ExSN5,
            "ExSN6" => EnumToneBank.ExSN6,
            "SRX01" => EnumToneBank.SRX01,
            "SRX02" => EnumToneBank.SRX02,
            "SRX03" => EnumToneBank.SRX03,
            "SRX04" => EnumToneBank.SRX04,
            "SRX05" => EnumToneBank.SRX05,
            "SRX06" => EnumToneBank.SRX06,
            "SRX07" => EnumToneBank.SRX07,
            "SRX08" => EnumToneBank.SRX08,
            "SRX09" => EnumToneBank.SRX09,
            "SRX10" => EnumToneBank.SRX10,
            "SRX11" => EnumToneBank.SRX11,
            "SRX12" => EnumToneBank.SRX12,
            "ExPCM" => EnumToneBank.ExPCM,
            _ => throw new MidiException("Invalid string value for tone bank: " + ToneBank)
        };
        _category = Category switch
        {
            "Ac.Piano" => EnumCategory.AcPiano,
            "Other Keyboards" => EnumCategory.OtherKeyboards,
            "Organ" => EnumCategory.Organ,
            "E.Guitar" => EnumCategory.EGuitar,
            "Dist.Guitar" => EnumCategory.DistGuitar,
            "Ac.Bass" => EnumCategory.AcBass,
            "E.Bass" => EnumCategory.EBass,
            "Ac.Guitar" => EnumCategory.AcGuitar,
            "Accordion/Harmonica" => EnumCategory.AccordionHarmonica,
            "Bell/Mallet" => EnumCategory.BellMallet,
            "Percussion" => EnumCategory.Percussion,
            "Plucked/Stroke" => EnumCategory.PluckedStroke,
            "Strings" => EnumCategory.Strings,
            "Vox/Choir" => EnumCategory.VoxChoir,
            "Brass" => EnumCategory.Brass,
            "Sax" => EnumCategory.Sax,
            "Wind" => EnumCategory.Wind,
            "Flute" => EnumCategory.Flute,
            "Synth Pad/Strings" => EnumCategory.SynthPadStrings,
            "Pulsating" => EnumCategory.Pulsating,
            "Synth Brass" => EnumCategory.SynthBrass,
            "Synth PolyKey" => EnumCategory.SynthPolyKey,
            "Synth Bellpad" => EnumCategory.SynthBellPad,
            "Synth Seq/Pop" => EnumCategory.SynthSeqPop,
            "Synth Bass" => EnumCategory.SynthBass,
            "Synth Lead" => EnumCategory.SynthLead,
            "FX" => EnumCategory.FX,
            "E.Piano" => EnumCategory.EPiano,
            "Hit" => EnumCategory.Hit,
            "Drums" => EnumCategory.Drums,
            "Beat&Groove" => EnumCategory.BeatGroove,
            "Recorder" => EnumCategory.Recorder,
            "Sound FX" => EnumCategory.SoundFX,
            "Phrase" => EnumCategory.Phrase,
            _ => throw new MidiException("Invalid string value for Category: " + Category)
        };
        _internalUserDefined = InternalUserDefined switch
        {
            "INT" => EnumInternalUserDefined.Internal,
            "USR" => EnumInternalUserDefined.UserDefined,
            _ => throw new MidiException("Invalid string value for InternalUserDefined: " + InternalUserDefined)
        };
    }

    // unique id
    private int _id { get; }
    public int Id => _id;

    // SN-A
    private EnumToneType _toneType { get; set; }
    private string _toneTypeStr { get; }
    public string ToneTypeStr => _toneTypeStr;
    private EnumToneBank _toneBank { get; set; }
    private string _toneBankStr { get; }
    public string ToneBankStr => _toneBankStr;
    private int _number { get; set; }
    private string _name { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    /// <summary>Raised when <see cref="Name"/> changes, which is what makes a user-slot rename show up
    /// in the preset grids. They bind <c>Name</c> directly (see PresetSelector.axaml), so without this
    /// the mutation lands on the object and nothing redraws.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _msb { get; }
    private int _lsb { get; }
    private int _pc { get; }
    private EnumCategory _category { get; set; }
    private string _categoryStr { get; }
    public string CategoryStr => _categoryStr;
    public int Msb => _msb;
    public int Lsb => _lsb;
    public int Pc => _pc;
    private EnumInternalUserDefined _internalUserDefined { get; }
    public string InternalUserDefinedStr => _internalUserDefined == EnumInternalUserDefined.Internal ? "INT" : "USR";
}