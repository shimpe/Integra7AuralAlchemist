using System.Linq;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>The nesting counter behind the "Syncing..." overlay.
///
/// It is entered and exited from thread-pool threads: the MessageBus handlers that drive it are
/// throttled, which schedules them off the UI thread, and they are async void, so a second one can
/// start while the first is still awaiting the device. A plain read-modify-write can therefore lose
/// an update, and a lost decrement leaves the overlay on screen with no way back.</summary>
[TestFixture]
public class TestSyncCounter
{
    [Test]
    public async Task PairedEntryAndExitFromManyThreadsReturnsToZero()
    {
        // `_syncLevels = _syncLevels + 1` loses updates reliably at this volume: every lost decrement
        // is an overlay that never disappears again.
        var counter = new SyncCounter();
        const int threads = 8;
        const int iterations = 5000;

        await Task.WhenAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                counter.Enter();
                counter.Exit();
            }
        })));

        Assert.That(counter.Level, Is.EqualTo(0));
        Assert.That(counter.Visible, Is.False, "the overlay must not be left on screen");
    }

    [Test]
    public void AnExitWithoutAMatchingEntryStaysAtZero()
    {
        // Happens when the program starts while the Integra-7 is off: the overlay begins visible, so
        // the first operation to finish exits a level it never entered.
        var counter = new SyncCounter();

        Assert.That(counter.Exit(), Is.EqualTo(0), "the level must not go negative");
        Assert.That(counter.Level, Is.EqualTo(0));
        Assert.That(counter.Visible, Is.False);
    }

    [Test]
    public void TheOverlayStaysVisibleUntilTheLastOperationFinishes()
    {
        var counter = new SyncCounter();

        Assert.That(counter.Enter(), Is.EqualTo(1));
        Assert.That(counter.Enter(), Is.EqualTo(2));
        Assert.That(counter.Visible, Is.True);

        Assert.That(counter.Exit(), Is.EqualTo(1));
        Assert.That(counter.Visible, Is.True, "an operation is still running");

        Assert.That(counter.Exit(), Is.EqualTo(0));
        Assert.That(counter.Visible, Is.False);
    }
}
