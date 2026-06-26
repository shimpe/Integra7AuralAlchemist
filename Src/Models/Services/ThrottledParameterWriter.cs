using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Per-key throttled write pipeline. Rapid writes sharing a key collapse to the last value
/// (THROTTLE-ms window); different keys are independent, so e.g. two envelope handles dragged
/// together both flush. Mirrors the Motional Surround editor's local write Subject.
/// </summary>
public sealed class ThrottledParameterWriter : IDisposable
{
    private sealed record Req(string Key, Func<Task> WriteAsync);

    private readonly Subject<Req> _subject = new();
    private readonly IDisposable _sub;

    public ThrottledParameterWriter(int throttleMs = Constants.THROTTLE, IScheduler? scheduler = null)
    {
        scheduler ??= Scheduler.Default;
        _sub = _subject
            .GroupBy(r => r.Key)
            .SelectMany(g => g.Throttle(TimeSpan.FromMilliseconds(throttleMs), scheduler))
            .Subscribe(async r =>
            {
                try { await r.WriteAsync(); }
                catch (Exception ex) { Log.Error(ex, "Throttled write failed for {Key}", r.Key); }
            });
    }

    public void Enqueue(string key, Func<Task> writeAsync) => _subject.OnNext(new Req(key, writeAsync));

    public void Dispose()
    {
        _sub.Dispose();
        _subject.Dispose();
    }
}
