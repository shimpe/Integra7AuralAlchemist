using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>The patch list as an MMA MIDINameDocument, which Ardour and Mixbus read.
///
/// <b>The one format here whose shape was never in doubt.</b> It is a published DTD rather than a
/// serialisation of somebody's object graph, and it is a schema about patches: a device, a name set, banks
/// carrying the two control changes that select them, and patches carrying a program change. Everything
/// below was checked against the files Ardour ships -- <c>share/patchfiles/Roland_SonicCell.midnam</c> in
/// particular, which describes an instrument addressed exactly the way this one is, MSB 87 with LSB 64
/// upwards.
///
/// <b>A patch carries two numbers and they are not the same number.</b> <c>ProgramChange</c> is the value
/// that goes on the wire, 0 to 127. <c>Number</c> is a label, one-based, restarting in each bank -- what
/// SonicCell writes, and what the instrument's own printed tone list counts in. Ardour does not display
/// Number at all; its parser skips the attribute with a comment saying it "is really more like a label and
/// is often not numeric". So putting the wire value in both would be invisible in the reader this format
/// was chosen for, which is exactly why it is worth a test: the mistake would survive every check anyone
/// made with Ardour open and would show up in whichever other reader does use the label.
///
/// <b>All sixteen channels, and the name set they are assigned to is assigned by name.</b> A part on this
/// instrument can be on any channel, and the assignment is a string that has to match the name set's own;
/// if the two ever disagree the document still parses and validates and offers every channel a name set
/// that is not there, which loses the entire patch list without a word.</summary>
public sealed class MidnamPatchListWriter : IPatchListWriter
{
    private const int Channels = 16;

    /// <summary>The DTD's public and system identifiers, verbatim from the files Ardour ships. The system
    /// identifier no longer resolves -- midi.org stopped serving it -- and that is not a problem to fix
    /// here: readers match on the public identifier, and a parser that fetched the system one over the
    /// network would be a worse thing than a dead link.</summary>
    private const string PublicId = "-//MIDI Manufacturers Association//DTD MIDINameDocument 1.0//EN";

    private const string SystemId = "http://www.midi.org/dtds/MIDINameDocument10.dtd";

    public string Label => "Ardour / Mixbus (.midnam)";
    public string Extension => "midnam";

    public string Write(PatchList list)
    {
        // One name set, named for the instrument, and the sixteen channel assignments refer to it by this
        // string. Held in a variable rather than written out twice so that the two cannot drift apart --
        // the failure that produces is a file that looks entirely correct and contains nothing.
        var nameSet = list.Device;

        var document = new XDocument(
            new XDocumentType("MIDINameDocument", PublicId, SystemId, null),
            new XElement("MIDINameDocument",
                new XElement("Author", "Integra-7 Aural Alchemist"),
                new XElement("MasterDeviceNames",
                    new XElement("Manufacturer", "Roland"),
                    new XElement("Model", list.Device),
                    new XElement("CustomDeviceMode", new XAttribute("Name", "Default"),
                        new XElement("ChannelNameSetAssignments",
                            Enumerable.Range(1, Channels).Select(channel =>
                                new XElement("ChannelNameSetAssign",
                                    new XAttribute("Channel", channel),
                                    new XAttribute("NameSet", nameSet))))),
                    new XElement("ChannelNameSet", new XAttribute("Name", nameSet),
                        new XElement("AvailableForChannels",
                            Enumerable.Range(1, Channels).Select(channel =>
                                new XElement("AvailableChannel",
                                    new XAttribute("Channel", channel),
                                    new XAttribute("Available", "true")))),
                        list.Banks.Select(Bank)))));

        // The declaration by hand, for the reason given in CubasePatchListWriter: an XmlWriter over a
        // StringWriter would declare UTF-16, which is what the StringWriter is and what nothing wants to
        // read. XDocument.ToString writes the doctype but not the declaration, so this is the only piece
        // that has to be said out loud.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document.ToString(SaveOptions.None) + "\n";
    }

    private static XElement Bank(PatchBank bank) =>
        new("PatchBank", new XAttribute("Name", bank.Name),
            new XElement("MIDICommands",
                Control(0, bank.Msb),
                Control(32, bank.Lsb)),
            new XElement("PatchNameList",
                // Numbered from one and restarting here, in the bank, because that is what the label means:
                // a position in this list, not a position in the instrument. Select's own index is the
                // right source for it precisely because it restarts with the sequence.
                bank.Patches.Select((patch, index) =>
                    new XElement("Patch",
                        new XAttribute("Number", index + 1),
                        new XAttribute("Name", patch.Name),
                        new XAttribute("ProgramChange", patch.Program)))));

    /// <summary>Control 0 is bank select MSB and control 32 is bank select LSB. Both are always written,
    /// even when one of them is nought: a bank select is two messages and a reader that saw only the MSB
    /// would leave whatever LSB the last patch change set, which selects a real bank that is the wrong
    /// one.</summary>
    private static XElement Control(int control, int value) =>
        new("ControlChange", new XAttribute("Control", control), new XAttribute("Value", value));
}
