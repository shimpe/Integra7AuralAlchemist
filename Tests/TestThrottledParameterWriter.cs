using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Services;
using Microsoft.Reactive.Testing;

namespace Tests;

public class ThrottledParameterWriterTests
{
    [Test]
    public void Same_key_collapses_to_last_value()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<int>();
        w.Enqueue("k", () => { sent.Add(1); return Task.CompletedTask; });
        w.Enqueue("k", () => { sent.Add(2); return Task.CompletedTask; });
        w.Enqueue("k", () => { sent.Add(3); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(251).Ticks);
        Assert.That(sent, Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void Different_keys_are_independent()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<string>();
        w.Enqueue("a", () => { sent.Add("a"); return Task.CompletedTask; });
        w.Enqueue("b", () => { sent.Add("b"); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(251).Ticks);
        Assert.That(sent, Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void Nothing_sent_before_the_throttle_window()
    {
        var scheduler = new TestScheduler();
        using var w = new ThrottledParameterWriter(250, scheduler);
        var sent = new List<int>();
        w.Enqueue("k", () => { sent.Add(1); return Task.CompletedTask; });
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
        Assert.That(sent, Is.Empty);
    }
}
