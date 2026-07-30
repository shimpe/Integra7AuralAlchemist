using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as a Cubase / Nuendo MIDI device setup, for the MIDI Device Manager's
/// "Import Setup".
///
/// <b>Where this shape came from.</b> Steinberg publish no schema for it, and it is not the tidy
/// patches-and-banks document one would design: it is their generic object-graph serialisation, in which
/// everything is an <c>&lt;obj class="..." ID="..."&gt;</c> carrying <c>&lt;string&gt;</c>,
/// <c>&lt;int&gt;</c>, <c>&lt;list&gt;</c> and <c>&lt;bin&gt;</c> members, and a patch list is what you get
/// by nesting the right classes. So it was read off a real exported file rather than invented:
/// <c>Steinberg XML Files/Fractal Axe Fx III.xml</c> in github.com/HarleyGaniere/Cubase-MIDI-Device-Scripts,
/// 10,330 lines exported by Cubase 12 on Windows -- the version is not claimed, it is visible in the file,
/// which carries a panel path under <c>AppData\Roaming\Steinberg\Cubase 12_64\Panels\</c>. The class names
/// used below (<c>PMidiParameterDevice</c>, <c>PMidiDeviceNode</c>, <c>PSoundscriptBank</c>,
/// <c>PMidiPreset</c>, <c>MidiSimpleKnownMessage</c>, <c>MidiStandardMessageFilter</c>) and the byte masks
/// are copied from it verbatim. Steinberg's own help confirms only the outline: the MIDI Device Manager
/// exports and imports setups as XML, and "Import Setup" is the function that reads them. The other file
/// Cubase will take, a tab-delimited .txt patch script dropped into its Patchnames folder, is a different
/// format for a different door and is not what this writes.
///
/// <b>What is inferred rather than observed, and how to check it.</b> Three things, and this file has not
/// been fed to Cubase -- nobody here has a copy. First, the bank-select <i>LSB</i> filter: the sample
/// carries filters for bank-select MSB (mask <c>B0F000FF0080</c>) and for breath LSB (<c>B0F022FF0080</c>),
/// and in that mask the third byte is the controller number -- 0x00 for CC 0, 0x22 for CC 34 -- so CC 32 is
/// <c>B0F020FF0080</c>. The pattern is unambiguous but those exact bytes were not seen. Second, the panel
/// and class-identifier machinery is left out: the sample's device nodes cite GUIDs that index a section of
/// encoded UI panels, and a patch list has no panels, so <c>NumberClassIDs</c> is nought here. Third, the
/// MIDI port: the sample names the exporting machine's own port, which would be wrong on anyone else's
/// machine, so no port is named and the user assigns one in the MIDI Device Manager as they would for any
/// device. If Cubase imports this and shows no names, those three are where to look, in that order.
///
/// <b>Written through XDocument, not by concatenating strings.</b> Patch names arrive from the instrument
/// and from the user's own memory, and 84 factory names alone carry a curly apostrophe; an ampersand in one
/// of them is enough to make the whole document fail to parse, which the user sees as an import that did
/// nothing. XElement escapes attribute content itself, including the newline that attribute-value
/// normalisation would otherwise turn into a space.</summary>
public sealed class CubasePatchListWriter : IPatchListWriter
{
    /// <summary>Every MIDI channel is offered the whole patch list. A part on this instrument can be on any
    /// of the sixteen, and a device whose names exist only on channel 1 looks entirely correct to whoever
    /// exported it and shows bare program numbers to whoever moved their part.</summary>
    private const int Channels = 16;

    /// <summary>The two controllers that select a bank, as this format spells a message: raw MIDI with the
    /// channel nibble left at nought, because a bank is written once and shared by all sixteen channel
    /// nodes and Cubase supplies the channel when it sends.</summary>
    private const int BankSelectMsb = 0x00, BankSelectLsb = 0x20;

    public string Label => "Cubase / Nuendo (.xml)";
    public string Extension => "xml";

    public string Write(PatchList list)
    {
        // Sequential rather than anything resembling the sample's memory addresses, and deliberately: two
        // exports of one instrument should be the same file, so that a diff of them says what changed in
        // the instrument rather than where the objects happened to land in memory. Nothing outside this
        // document ever sees these numbers -- they exist only so that one part of it can cite another.
        var ids = new Ids();

        var msb = Filter(ids, "CC: BankSelect MSB", "B0F000FF0080", ControllerValue(ids));
        var lsb = Filter(ids, "CC: BankSelect LSB", "B0F020FF0080", ControllerValue(ids));
        var program = Filter(ids, "Program Change", "C0F00080", ProgramValue(ids));

        var banksId = ids.Next();
        var banks = new XElement("obj",
            new XAttribute("class", "PSoundscriptBank"), new XAttribute("ID", banksId),
            Text("PresetBankName", list.Device, wide: true),
            Text("IDString", list.Device),
            ObjectList("Children", list.Banks.Select(bank => Bank(ids, bank, msb, lsb, program))));

        // The bank tree hangs off the first channel node and the other fifteen cite it, which is what the
        // sample does and is not merely tidiness: writing it out sixteen times would be sixteen copies of
        // 6,023 patches, and Cubase would show the user sixteen unrelated devices' worth of banks rather
        // than one instrument reachable on any channel.
        // Forced, not left lazy: handing out an identifier is a side effect, and a sequence that allocates
        // them would hand out a second set if anything ever enumerated it twice. Nothing does today.
        var channels = Enumerable.Range(0, Channels)
            .Select(channel => ChannelNode(ids, channel, channel == 0 ? banks : Cite(banksId)))
            .ToList();

        var device = new XElement("obj",
            new XAttribute("class", "PMidiParameterDevice"), new XAttribute("ID", ids.Next()),
            Text("DeviceNode Name", list.Device, wide: true),
            Text("ClassName", "", wide: true),
            Text("IDString", list.Device),
            ObjectList("Children", [PortNode(ids, list.Device, channels)]),
            Number("NodeFlags", 8),
            Number("NumberClassIDs", 0),
            ObjectList("Banks", [Cite(banksId)]));

        // The three filters go last, after the device that cites them, because that is where the sample
        // puts them: the reader resolves citations by ID once the whole document is parsed, so it reads
        // forwards happily. Moving them to the front would make every citation point backwards and would
        // read better -- and would also be the one part of this file's layout that no observed file
        // supports, which is not a trade worth making for tidiness.
        var document = new XDocument(new XElement("MidiDevices",
            ObjectList("Devices", [device]), msb, lsb, program));

        // The declaration is written by hand because the alternatives are worse: XmlWriter over a
        // StringWriter declares UTF-16, which is what the StringWriter really is and what nothing wants to
        // read, and subclassing StringWriter to lie about its Encoding is a well-known workaround for a
        // problem this does not have. The bytes are decided by whoever writes the file; what is decided
        // here is what the document says about itself, and it says UTF-8 because that is what it will be
        // written as.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document.ToString(SaveOptions.None) + "\n";
    }

    /// <summary>The device node that would name a MIDI port. It names none: the sample was exported with
    /// its author's own port in it, and a file that arrived naming someone else's hardware is a file the
    /// user has to notice and correct before it works.</summary>
    private static XElement PortNode(Ids ids, string device, IEnumerable<XElement> channels) =>
        new("obj", new XAttribute("class", "PMidiDeviceNode"), new XAttribute("ID", ids.Next()),
            Text("DeviceNode Name", device, wide: true),
            Text("ClassName", "MidiDeviceNode"),
            Text("IDString", device),
            ObjectList("Children", channels),
            Number("NodeFlags", 0),
            Number("NumberClassIDs", 0),
            Number("DefChannel", -1),
            Number("NumberParameters", 0),
            Number("NumberVariableStates", 0),
            Number("HasInputFromDevice", 0),
            Number("IsPortRoot", 1),
            Number("NumberVariables", 0));

    private static XElement ChannelNode(Ids ids, int channel, XElement banks) =>
        new("obj", new XAttribute("class", "PMidiDeviceNode"), new XAttribute("ID", ids.Next()),
            Text("DeviceNode Name", $"Channel {channel + 1}", wide: true),
            Text("ClassName", "MidiDeviceNode"),
            Text("IDString", $"Channel {channel + 1}"),
            Number("NodeFlags", 0),
            Number("NumberClassIDs", 0),
            Number("DefChannel", channel),
            Number("NumberParameters", 0),
            Number("NumberVariableStates", 0),
            Number("IsChannelNode", 1),
            Number("HasInputFromDevice", 0),
            ObjectList("Banks", [banks]),
            Number("IsPortRoot", 0),
            Number("NumberVariables", 0));

    /// <summary>The bank's name is its identifier as well as its label, which is safe only because
    /// <see cref="PatchListSource"/> puts the address in it: 51 of the 75 factory banks share an engine and
    /// a bank name with another, and this format has nowhere else to tell them apart.</summary>
    private static XElement Bank(Ids ids, PatchBank bank, XElement msb, XElement lsb, XElement program) =>
        new("obj", new XAttribute("class", "PSoundscriptBank"), new XAttribute("ID", ids.Next()),
            Text("PresetBankName", bank.Name, wide: true),
            Text("IDString", bank.Name),
            ObjectList("Presets",
                bank.Patches.Select(patch => Preset(ids, bank, patch, msb, lsb, program))));

    private static XElement Preset(Ids ids, PatchBank bank, PatchEntry patch,
        XElement msb, XElement lsb, XElement program) =>
        new("obj", new XAttribute("class", "PMidiPreset"), new XAttribute("ID", ids.Next()),
            // wide, because this is the one string here that came from the instrument rather than from
            // this file: 84 factory names carry a curly apostrophe and a user may have typed anything.
            Text("Name", patch.Name, wide: true),
            Number("CountMsgs", 3),
            ObjectList("Messages",
            [
                Message(ids, msb, $"B0{BankSelectMsb:X2}{bank.Msb:X2}"),
                Message(ids, lsb, $"B0{BankSelectLsb:X2}{bank.Lsb:X2}"),
                // The wire value, already converted once in PatchListSource. Nothing here adds or subtracts
                // one, and the test that pins C000 for program 0 is what says so.
                Message(ids, program, $"C0{patch.Program:X2}"),
            ]),
            Number("CountAttKeys", 0));

    /// <summary>One MIDI message, and the filter that says what its bytes mean.
    ///
    /// <b>The Creator is not decoration.</b> The bytes of a bank-select MSB and a bank-select LSB differ in
    /// one nibble, and Cubase reads a message through the filter it cites; a message whose bytes say one
    /// thing and whose Creator says another is a document that imports without complaint and selects the
    /// wrong sound.</summary>
    private static XElement Message(Ids ids, XElement creator, string bytes) =>
        new("obj", new XAttribute("class", "MidiSimpleKnownMessage"), new XAttribute("ID", ids.Next()),
            new XElement("obj", new XAttribute("name", "Creator"), creator.Attribute("ID")),
            new XElement("bin", new XAttribute("name", "Message"), bytes));

    /// <summary>A message filter: a human-readable name, and a mask of (value, mask) byte pairs saying
    /// which bytes are fixed and which carry a number. <c>B0F000FF0080</c> reads as status B0 under mask
    /// F0 -- a control change on any channel -- controller 00 exactly, and any 7-bit value.</summary>
    private static XElement Filter(Ids ids, string info, string mask, XElement value) =>
        new("obj", new XAttribute("class", "MidiStandardMessageFilter"), new XAttribute("ID", ids.Next()),
            Text("Info", info, wide: true),
            Number("Tag", -1),
            new XElement("obj",
                new XAttribute("class", "MidiSimpleMessageFilter"), new XAttribute("name", "Filter"),
                new XAttribute("ID", ids.Next()),
                ObjectList("Values", [Channel(ids), value]),
                new XElement("bin", new XAttribute("name", "Mask"), mask)));

    /// <summary>The channel nibble: four bits at byte 0. Written out fresh in each filter rather than
    /// declared once and cited, because three copies of six lines is cheaper to read than a citation that
    /// has to be chased, and nothing in the format objects.</summary>
    private static XElement Channel(Ids ids) => Value(ids, type: 0, tag: 0, bits: 4, position: 0);

    /// <summary>The value byte of a control change: seven bits at byte 2, tag 9.</summary>
    private static XElement ControllerValue(Ids ids) => Value(ids, type: 256, tag: 9, bits: 7, position: 2);

    /// <summary>The number in a program change: seven bits at byte 1, tag 5.</summary>
    private static XElement ProgramValue(Ids ids) => Value(ids, type: 256, tag: 5, bits: 7, position: 1);

    private static XElement Value(Ids ids, int type, int tag, int bits, int position) =>
        new("obj", new XAttribute("class", "MidiSimpleValue"), new XAttribute("ID", ids.Next()),
            Number("Type", type),
            Number("Tag", tag),
            Number("Bits", bits),
            Number("BitsPerByte", bits),
            // Four slots because the format allows a value spread over up to four bytes; -1 is "not used".
            new XElement("list", new XAttribute("name", "Pos"), new XAttribute("type", "int"),
                new XElement("item", new XAttribute("value", position)),
                new XElement("item", new XAttribute("value", -1)),
                new XElement("item", new XAttribute("value", -1)),
                new XElement("item", new XAttribute("value", -1))),
            Text("UserName", "", wide: true));

    /// <summary>A string member. <c>wide</c> marks it as one Cubase stores as wide characters, which is
    /// what the sample does for everything the user is shown and not for the internal identifiers; the
    /// names from the instrument are the ones that need it.</summary>
    private static XElement Text(string name, string value, bool wide = false) =>
        wide
            ? new XElement("string", new XAttribute("name", name), new XAttribute("value", value),
                new XAttribute("wide", "true"))
            : new XElement("string", new XAttribute("name", name), new XAttribute("value", value));

    private static XElement Number(string name, int value) =>
        new("int", new XAttribute("name", name), new XAttribute("value", value));

    private static XElement ObjectList(string name, IEnumerable<object> content) =>
        new("list", new XAttribute("name", name), new XAttribute("type", "obj"), content);

    /// <summary>A citation, in a list, of an object declared elsewhere. The format has a second spelling
    /// for a citation that is a named member -- an &lt;obj&gt; with an ID and no class, which is what
    /// <see cref="Message"/> writes for its Creator -- and chooses between the two by position rather than
    /// by meaning.</summary>
    private static XElement Cite(int id) => new("item", new XAttribute("value", id));

    /// <summary>Numbers that only have to be unique inside one document, handed out in the order the
    /// document is built so that the same instrument always produces the same file.</summary>
    private sealed class Ids
    {
        private int _next = 1;

        public int Next() => _next++;
    }
}
