using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;
using Integra7AuralAlchemist.Models.Services;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI.Builder;

namespace Tests;

/// <summary>What the Compare tab shows, and that what it exports says the same thing.
///
/// The tab and the exported text drifted apart once already on this branch -- the summary line was written
/// twice and only one copy learned a rule -- so the values are pinned on both at once, from one comparison.
/// </summary>
public class CompareViewModelTests
{
    private readonly Integra7Parameters _parameters =
        new(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "Src", "Assets", "parameters.bin")));

    /// <summary>The view model's constructor subscribes to its own search box, and ReactiveUI's
    /// WhenAnyValue refuses to run until its services are registered -- which the application does on
    /// startup and a plain test runner does not. Core services only: nothing here touches a platform.
    /// </summary>
    [OneTimeSetUp]
    public void InitialiseReactiveUi() =>
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();

    /// <summary>A tone whose stored category display is the bare number, which is what every file written
    /// before this build had a table for it holds.</summary>
    private static Integra7Snapshot Tone(string name, long category) =>
        new(Integra7Snapshot.CurrentFormatVersion, name,
            [
                new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                    "Offset2/SuperNATURAL Synth Tone Common",
                    [
                        new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Category",
                            $"{category}", category),
                    ]),
            ],
            SnapshotKinds.Tone, "SN-S");

    private static Task<(Integra7Snapshot Snapshot, string Source)?> Nothing() =>
        Task.FromResult<(Integra7Snapshot, string)?>(null);

    private CompareViewModel Tab(List<string> copied) =>
        new(_parameters, Nothing, Nothing, _ => Nothing(),
            text =>
            {
                copied.Add(text);
                return Task.CompletedTask;
            },
            _ => Task.FromResult<string?>(null),
            (_, _) => { });

    [Test]
    public async Task A_value_named_since_the_files_were_written_is_named_on_screen_and_in_the_export()
    {
        List<string> copied = [];
        var tab = Tab(copied);
        tab.PutInFirstFreeSlot(Tone("a", 36), "file a");
        tab.PutInFirstFreeSlot(Tone("b", 34), "file b");

        tab.Compare();

        var row = tab.Blocks.Single().Rows.Single();
        Assert.That(row.LeftValue, Is.EqualTo("Synth Pad/Strings"));
        Assert.That(row.RightValue, Is.EqualTo("Synth Lead"));

        await tab.CopyAsync();

        Assert.That(copied.Single(), Does.Contain("Synth Pad/Strings"));
        Assert.That(copied.Single(), Does.Contain("Synth Lead"));
    }

    /// <summary>A parameter one snapshot carries and the other does not is a finding, and it has to be on
    /// screen. It was computed and exported from the start but the tab showed differences alone, so it
    /// appeared nowhere -- which reads as the comparison having missed it, in exactly the case a
    /// comparison is most wanted for.</summary>
    [Test]
    public void A_parameter_only_one_side_carries_is_shown_with_the_value_the_side_that_has_it_holds()
    {
        var withSwitch = new Integra7Snapshot(Integra7Snapshot.CurrentFormatVersion, "a",
            [
                new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                    "Offset2/SuperNATURAL Synth Tone Common",
                    [
                        new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100),
                        new SnapshotValue("SuperNATURAL Synth Tone Common/Partial1 Switch", "ON", 1),
                    ]),
            ],
            SnapshotKinds.Tone, "SN-S");

        var without = withSwitch with
        {
            Name = "b",
            Domains =
            [
                new SnapshotDomain("Temporary Tone Part 1", "Offset/Temporary SuperNATURAL Synth Tone",
                    "Offset2/SuperNATURAL Synth Tone Common",
                    [new SnapshotValue("SuperNATURAL Synth Tone Common/Tone Level", "100", 100)]),
            ],
        };

        var tab = Tab([]);
        tab.PutInFirstFreeSlot(withSwitch, "file a");
        tab.PutInFirstFreeSlot(without, "file b");

        tab.Compare();

        var row = tab.Blocks.Single().Rows
            .Single(r => r.Path.EndsWith("Partial1 Switch"));
        Assert.That(row.LeftValue, Is.EqualTo("ON"), "the value the side that has it holds");
        Assert.That(row.RightValue, Does.Contain("not in this snapshot"));
    }
}
