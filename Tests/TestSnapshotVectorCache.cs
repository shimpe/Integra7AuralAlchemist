using System;
using System.Collections.Generic;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Tests;

/// <summary>What a duplicate scan may take from the last scan, and what it has to read again.</summary>
public class SnapshotVectorCacheTests
{
    private static readonly DateTime Monday = new(2026, 7, 27, 10, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Tuesday = new(2026, 7, 28, 10, 0, 0, DateTimeKind.Local);

    private static SnapshotFileStamp File(string path, DateTime modified, long length = 100) =>
        new(path, modified, length);

    private static RawVector Vector(params long[] values) => new(SnapshotKinds.Tone, "SN-S", values);

    /// <summary>A scan, as the view model runs one: ask what has to be opened, open exactly those, hand back
    /// what they held. <paramref name="unreadable"/> is the file the loop failed on and therefore never
    /// offers.</summary>
    private static (IReadOnlyList<(string Path, RawVector Vector)> Entries, List<string> Opened) Scan(
        SnapshotVectorCache cache, IReadOnlyList<SnapshotFileStamp> folder,
        Func<string, RawVector?> contents, string? unreadable = null)
    {
        var opened = cache.ToRead(folder).Select(file => file.Path).ToList();
        List<(string, RawVector?)> read = [];
        foreach (var path in opened.Where(path => path != unreadable)) read.Add((path, contents(path)));
        return (cache.Vectors(folder, read), opened);
    }

    [Test]
    public void Nothing_is_known_before_the_first_scan()
    {
        var cache = new SnapshotVectorCache();

        var (entries, opened) = Scan(cache, [File("a.json", Monday), File("b.json", Monday)],
            _ => Vector(1, 2));

        Assert.That(opened, Is.EqualTo(new[] { "a.json", "b.json" }));
        Assert.That(entries.Select(e => e.Path), Is.EqualTo(new[] { "a.json", "b.json" }));
    }

    /// <summary>The whole point: a second scan of an untouched folder opens nothing and still answers for
    /// every file in it.</summary>
    [Test]
    public void A_file_that_has_not_moved_is_not_read_again()
    {
        var cache = new SnapshotVectorCache();
        var folder = new[] { File("a.json", Monday) };
        Scan(cache, folder, _ => Vector(1, 2));

        var (entries, opened) = Scan(cache, folder, _ => throw new InvalidOperationException("re-read"));

        Assert.That(opened, Is.Empty);
        Assert.That(entries.Single().Vector.Values, Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void A_file_written_since_is_read_again()
    {
        var cache = new SnapshotVectorCache();
        Scan(cache, [File("a.json", Monday)], _ => Vector(1, 2));

        var (entries, opened) = Scan(cache, [File("a.json", Tuesday)], _ => Vector(3, 4));

        Assert.That(opened, Is.EqualTo(new[] { "a.json" }));
        Assert.That(entries.Single().Vector.Values, Is.EqualTo(new long[] { 3, 4 }));
    }

    /// <summary>The half of the stamp that is not the clock. A file system's last-write time is coarse enough
    /// that a rewrite can land inside the same tick as the read before it, and a length is free to compare.
    /// </summary>
    [Test]
    public void A_file_of_a_different_length_is_read_again_even_at_the_same_time()
    {
        var cache = new SnapshotVectorCache();
        Scan(cache, [File("a.json", Monday, length: 100)], _ => Vector(1, 2));

        var (entries, opened) = Scan(cache, [File("a.json", Monday, length: 140)], _ => Vector(3, 4));

        Assert.That(opened, Is.EqualTo(new[] { "a.json" }));
        Assert.That(entries.Single().Vector.Values, Is.EqualTo(new long[] { 3, 4 }));
    }

    /// <summary>A file that has left the folder is forgotten, so the cache cannot outgrow the library -- and
    /// a path that comes back is a fresh question however it is stamped.</summary>
    [Test]
    public void A_file_that_has_left_the_folder_is_forgotten()
    {
        var cache = new SnapshotVectorCache();
        var stamp = File("a.json", Monday);
        Scan(cache, [stamp], _ => Vector(1, 2));

        Scan(cache, [], _ => Vector(1, 2));
        var (_, opened) = Scan(cache, [stamp], _ => Vector(1, 2));

        Assert.That(opened, Is.EqualTo(new[] { "a.json" }));
    }

    /// <summary>A file out of the folder is out of the answer, whatever the cache still holds -- the scan
    /// only ever asks about what is there now.</summary>
    [Test]
    public void A_file_out_of_the_folder_is_out_of_the_answer()
    {
        var cache = new SnapshotVectorCache();
        Scan(cache, [File("a.json", Monday), File("b.json", Monday)], _ => Vector(1, 2));

        var (entries, _) = Scan(cache, [File("b.json", Monday)], _ => Vector(1, 2));

        Assert.That(entries.Select(e => e.Path), Is.EqualTo(new[] { "b.json" }));
    }

    /// <summary>This phase's own workflow: delete a duplicate, save the sound again, and the freed name is
    /// handed straight back by <c>SnapshotLibrary.UniquePath</c>, which only avoids a name that is taken at
    /// the time. The path is the same and the sound is not, so the answer must be the new one.</summary>
    [Test]
    public void A_path_freed_by_a_delete_and_taken_by_another_save_is_read_again()
    {
        var cache = new SnapshotVectorCache();
        Scan(cache, [File("Warm Rhodes.json", Monday, length: 100)], _ => Vector(1, 2));

        var (entries, opened) = Scan(cache, [File("Warm Rhodes.json", Tuesday, length: 240)],
            _ => Vector(9, 9));

        Assert.That(opened, Is.EqualTo(new[] { "Warm Rhodes.json" }));
        Assert.That(entries.Single().Vector.Values, Is.EqualTo(new long[] { 9, 9 }));
    }

    /// <summary>Something in the folder that is not a snapshot: never in the answer, and remembered as such,
    /// because a library folder is a folder and re-opening everybody else's JSON on every scan is the cost
    /// this cache exists to avoid.</summary>
    [Test]
    public void Something_that_is_not_a_snapshot_is_remembered_as_one_that_is_not()
    {
        var cache = new SnapshotVectorCache();
        var folder = new[] { File("a.json", Monday), File("config.json", Monday) };

        var (first, _) = Scan(cache, folder, path => path == "config.json" ? null : Vector(1, 2));
        var (second, opened) = Scan(cache, folder, _ => throw new InvalidOperationException("re-read"));

        Assert.That(first.Select(e => e.Path), Is.EqualTo(new[] { "a.json" }));
        Assert.That(second.Select(e => e.Path), Is.EqualTo(new[] { "a.json" }));
        Assert.That(opened, Is.Empty);
    }

    /// <summary>A file that could not be opened at all -- held by a sync client, denied -- is left out of the
    /// answer and is <b>not</b> remembered. Pressing Scan again is the user's whole remedy for that, and a
    /// cache that had recorded the failure would make it do nothing.</summary>
    [Test]
    public void A_file_that_could_not_be_read_is_offered_again_next_time()
    {
        var cache = new SnapshotVectorCache();
        var folder = new[] { File("a.json", Monday), File("locked.json", Monday) };

        var (first, _) = Scan(cache, folder, _ => Vector(1, 2), unreadable: "locked.json");
        var (second, opened) = Scan(cache, folder, _ => Vector(1, 2));

        Assert.That(first.Select(e => e.Path), Is.EqualTo(new[] { "a.json" }));
        Assert.That(opened, Is.EqualTo(new[] { "locked.json" }));
        Assert.That(second.Select(e => e.Path), Is.EqualTo(new[] { "a.json", "locked.json" }));
    }

    /// <summary>One row per file, whatever the caller hands in. Two rows for one path would be a file grouped
    /// with itself, which is the one thing a duplicate report must never say.</summary>
    [Test]
    public void A_path_listed_twice_is_answered_once()
    {
        var cache = new SnapshotVectorCache();

        var (entries, _) = Scan(cache, [File("a.json", Monday), File("a.json", Monday)], _ => Vector(1, 2));

        Assert.That(entries.Select(e => e.Path), Is.EqualTo(new[] { "a.json" }));
    }

    /// <summary>Paths are compared the way the rest of the library compares them, so a folder listed as
    /// "A.json" one time and "a.json" the next is one file rather than two.</summary>
    [Test]
    public void Paths_are_compared_without_regard_to_case()
    {
        var cache = new SnapshotVectorCache();
        Scan(cache, [File("A.json", Monday)], _ => Vector(1, 2));

        var (entries, opened) = Scan(cache, [File("a.json", Monday)],
            _ => throw new InvalidOperationException("re-read"));

        Assert.That(opened, Is.Empty);
        Assert.That(entries, Has.Count.EqualTo(1));
    }
}
