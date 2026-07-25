using System;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>FullyQualifiedParameter.ApplyRawValue against the real parameter database. Raw is the form
/// the device stores and the form a Studio Set snapshot restores from, so applying one has to leave the
/// parameter in exactly the state a read of that same raw value from the device would have left it in --
/// for every shape of parameter the database contains, not just the simple ones. Every expected display
/// string below is derived from the parameter's own definition (its imin/imax -> omin/omax mapping and
/// its repr table), never from what the code happened to produce.</summary>
[TestFixture]
public class ApplyRawValueTests
{
    private static Integra7Parameters? _parameters;

    private static FullyQualifiedParameter Parameter(string path)
    {
        _parameters ??= TestFailedReadKeepsValues.LoadParameters();
        // The addresses are irrelevant here: nothing in this fixture touches a device.
        return new FullyQualifiedParameter("Temporary Studio Set", "Offset/Not Used",
            "Offset2/Studio Set Common", _parameters.Lookup(path));
    }

    /// <summary>Applying the raw value a parameter already holds must change nothing, and applying a
    /// different one must produce the display string that raw corresponds to. The starting value is set
    /// the ordinary way -- through the display-string converter the UI uses -- so the fixed point being
    /// asserted is a state the application really reaches, not one only this test can construct.</summary>
    private static void AssertRoundTrips(string path, string startDisplay, long startRaw,
        long otherRaw, string otherDisplay)
    {
        var p = Parameter(path);
        DisplayValueToRawValueConverter.UpdateFromDisplayedValue(startDisplay, p);
        Assert.That(p.RawNumericValue, Is.EqualTo(startRaw), $"{path}: display -> raw");

        p.ApplyRawValue(p.RawNumericValue);

        Assert.That(p.RawNumericValue, Is.EqualTo(startRaw), $"{path}: re-applying its own raw changed it");
        Assert.That(p.StringValue, Is.EqualTo(startDisplay), $"{path}: re-applying its own raw changed the display");

        p.ApplyRawValue(otherRaw);

        Assert.That(p.RawNumericValue, Is.EqualTo(otherRaw), $"{path}: applying a new raw");
        Assert.That(p.StringValue, Is.EqualTo(otherDisplay), $"{path}: display for the new raw");
    }

    [Test]
    public void A_plain_numeric_parameter_round_trips()
    {
        // Part Level: 0..127 in, 0..127 out, one byte, no repr -- the display string is the raw value.
        var p = Parameter("Studio Set Part/Part Level");
        Assert.That(p.ParSpec.Repr, Is.Null);
        Assert.That(p.ParSpec.Bytes, Is.EqualTo(1));

        AssertRoundTrips("Studio Set Part/Part Level", "100", 100, 64, "64");
    }

    [Test]
    public void A_numeric_parameter_with_an_offset_mapping_round_trips()
    {
        // Part Pan: 0..127 raw maps to -64..63 for display, so raw and display are never the same
        // number and a mapping dropped anywhere in the round trip shows up immediately.
        var p = Parameter("Studio Set Part/Part Pan");
        Assert.That((p.ParSpec.IMin, p.ParSpec.IMax, p.ParSpec.OMin, p.ParSpec.OMax), Is.EqualTo((0, 127, -64f, 63f)));

        AssertRoundTrips("Studio Set Part/Part Pan", "0", 64, 127, "63");
    }

    [Test]
    public void A_numeric_parameter_with_a_repr_round_trips()
    {
        // Reverb Type: the enum-table case, and the one that motivated storing raw at all -- these
        // strings are presentation, the raw index is what the device holds.
        var p = Parameter("Studio Set Common Reverb/Reverb Type");
        Assert.That(p.ParSpec.Repr, Is.Not.Null);
        Assert.That(p.ParSpec.Repr![1], Is.EqualTo("Room1"));
        Assert.That(p.ParSpec.Repr[4], Is.EqualTo("Hall 2"));

        AssertRoundTrips("Studio Set Common Reverb/Reverb Type", "Room1", 1, 4, "Hall 2");
    }

    [Test]
    public void A_nibbled_multi_byte_parameter_round_trips()
    {
        // Studio Set Tempo: two bytes, one nibble each. A raw value assembled or split wrongly comes
        // back as a different number entirely, so 137 (0x89, two unequal nibbles) is deliberate --
        // 240 alone would survive a swapped nibble order.
        var p = Parameter("Studio Set Common/Studio Set Tempo");
        Assert.That(p.ParSpec.PerNibble, Is.True);
        Assert.That(p.ParSpec.Bytes, Is.EqualTo(2));

        AssertRoundTrips("Studio Set Common/Studio Set Tempo", "240", 240, 137, "137");
    }

    [Test]
    public void A_numeric_parameter_with_a_secondary_mapping_round_trips()
    {
        // Delay Center Feedback: four nibbled bytes through two chained mappings -- 12768..52768 to
        // -20000..20000, then 0..98 to -98..98. Raw 32817 is display 0 and raw 32827 is display 20;
        // neither survives if only the first mapping is applied.
        const string path = "Studio Set Common Chorus/Chorus Parameter 10/Delay Center Feedback";
        var p = Parameter(path);
        Assert.That((p.ParSpec.IMin2, p.ParSpec.IMax2, p.ParSpec.OMin2, p.ParSpec.OMax2),
            Is.EqualTo((0f, 98f, -98f, 98f)));
        Assert.That(p.ParSpec.PerNibble, Is.True);
        Assert.That(p.ParSpec.Bytes, Is.EqualTo(4));

        AssertRoundTrips(path, "0", 32817, 32827, "20");
    }

    [Test]
    public void A_discrete_parameter_round_trips()
    {
        // The only discrete parameter in the database. Its raw values are not a dense range (0x4000,
        // 0x4001, ... but also 0x0004, 0x0104), which is exactly why they are stored as a lookup list
        // and why nothing may try to derive one from the other.
        const string path = "SuperNATURAL Acoustic Tone Common/Instrument";
        var p = Parameter(path);
        Assert.That(p.IsDiscrete, Is.True);
        Assert.That(p.ParSpec.Discrete, Is.Not.Null);

        AssertRoundTrips(path, "INT 001: Concert Grand", 0x4000, 0x4002, "INT 003: Grand Piano 2");
    }

    [Test]
    public void Refuses_a_text_parameter()
    {
        // A text parameter has no raw form at all -- its value IS the string -- so there is nothing
        // ApplyRawValue could sensibly do. It throws rather than no-opping: a silent no-op would let a
        // caller believe it applied a value while the parameter kept the old one, which is the quiet
        // wrong-data failure raw values exist to prevent. Callers can always tell (IsNumeric/IsDiscrete).
        var p = Parameter("Studio Set Common/Studio Set Name");
        Assert.That(p.IsNumeric, Is.False);
        Assert.That(p.IsDiscrete, Is.False);
        p.StringValue = "World Pop Set";

        var e = Assert.Throws<InvalidOperationException>(() => p.ApplyRawValue(42));

        Assert.That(e!.Message, Does.Contain("Studio Set Common/Studio Set Name"));
        Assert.That(p.StringValue, Is.EqualTo("World Pop Set"), "the refused call must not have changed anything");
    }

    [Test]
    public void Notifies_that_both_values_changed()
    {
        // The grids bind to these through DynamicData's AutoRefresh, which only re-evaluates on
        // INotifyPropertyChanged. Assigning the backing field directly (which ApplyRawValue does, to
        // encode the sysex fragment) would leave the screen showing the old value.
        var p = Parameter("Studio Set Part/Part Level");
        var changed = new System.Collections.Generic.List<string?>();
        p.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        p.ApplyRawValue(77);

        Assert.That(changed, Does.Contain(nameof(FullyQualifiedParameter.RawNumericValue)));
        Assert.That(changed, Does.Contain(nameof(FullyQualifiedParameter.StringValue)));
    }
}
