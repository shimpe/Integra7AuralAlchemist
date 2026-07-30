using System;
using System.Collections.Generic;
using System.Linq;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Which snapshots are the same sound saved more than once.
///
/// <b>Near, not identical, and that is the case worth catching.</b> Exact duplicates happen -- a file copied
/// in twice -- but the complaint this answers is the sound saved four times while it was being edited, and
/// those differ by a handful of parameters. So the measure is a count of differing values and the user sets
/// the bar.
///
/// <b>Buckets first.</b> Nothing is compared across a kind, an engine or a vector length. The same position
/// in two engines' vectors is two different parameters, so a count across them would be a number with no
/// meaning; two lengths of the same engine are two builds of this application, one of which knew a
/// parameter the other did not, and lining those up positionally would mismatch everything after the first
/// difference. Bucketing is also what makes the pairwise comparison affordable -- that, and abandoning a
/// pair the moment it passes the threshold.
///
/// <b>Grouping is transitive, deliberately.</b> A near B and B near C puts all three together even where A
/// and C differ by more than the threshold. The alternative -- only reporting pairs -- would show the same
/// patch in three rows and leave the user to work out it was one family. What the panel must therefore say
/// is "each differs in at most N from at least one other here", not that every pair is alike.
///
/// The vectors come from <see cref="SnapshotRawVector"/>, whose remarks hold the assumption all of this
/// rests on: two files of one engine yield vectors that line up position by position.</summary>
public static class DuplicateGroups
{
    /// <summary>The groups, each two or more paths. Ordered so that two scans of one folder present the
    /// same list: within a group by path, and between groups by their first path.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Find(
        IReadOnlyList<(string Path, RawVector Vector)> entries, int threshold)
    {
        List<List<string>> groups = [];

        foreach (var bucket in entries.GroupBy(e => (e.Vector.Kind, e.Vector.ToneType, e.Vector.Values.Length)))
        {
            var members = bucket.ToList();

            // Each member points at another member of its family, or at itself. The groups are read off at
            // the end by asking every member who it ultimately points at.
            //
            // <b>This replaced a version that kept a list of groups and an index per member.</b> That one
            // had to join two families by folding one list into another and rewriting every index that
            // pointed at it -- and whether it emptied the spare slot or removed it made no difference any
            // test could see, which is a bad property for the one line where members can be silently lost.
            // Pointing at a member rather than at a list position removes the question: there are no
            // positions to go stale.
            var parent = new int[members.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;

            int Family(int i)
            {
                while (parent[i] != i) i = parent[i] = parent[parent[i]];
                return i;
            }

            for (var i = 0; i < members.Count; i++)
            for (var j = i + 1; j < members.Count; j++)
            {
                if (!Alike(members[i].Vector.Values, members[j].Vector.Values, threshold)) continue;

                // Joining two families is one assignment, whether or not either already had members.
                var (a, b) = (Family(i), Family(j));
                if (a != b) parent[b] = a;
            }

            groups.AddRange(members
                .Select((member, index) => (member.Path, Family: Family(index)))
                .GroupBy(x => x.Family)
                .Where(family => family.Count() > 1)
                .Select(family => family.Select(x => x.Path).ToList()));
        }

        foreach (var group in groups) group.Sort(StringComparer.OrdinalIgnoreCase);

        return [.. groups.OrderBy(g => g[0], StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Whether two vectors differ in at most <paramref name="threshold"/> positions.
    ///
    /// <b>It gives up as soon as it knows the answer is no</b>, which is what makes comparing every pair in
    /// a bucket acceptable: two patches that are nothing like each other cost a handful of comparisons
    /// rather than fifteen hundred.</summary>
    private static bool Alike(long[] a, long[] b, int threshold)
    {
        var differences = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (++differences > threshold) return false;
        }

        return true;
    }
}
