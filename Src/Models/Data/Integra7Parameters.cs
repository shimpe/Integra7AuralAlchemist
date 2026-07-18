using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia.Platform;

namespace Integra7AuralAlchemist.Models.Data;

// Runtime access to the Integra-7 parameter database. The data itself lives in a compact binary blob
// (Assets/parameters.bin), generated at build time from the C# definitions in
// Tools/ParameterBlobGenerator and loaded once here into a columnar ParameterStore. Specs are exposed
// as zero-allocation Integra7ParameterSpec struct-views.
public class Integra7Parameters
{
    private readonly ParameterStore _store;
    private readonly Dictionary<string, int> _index = new();

    /// <summary>Results of <see cref="GetParametersWithPrefix"/>, keyed by prefix. Building the domains
    /// asks for ~3000 prefix lookups but uses only ~29 distinct prefixes (the prefix names a kind of
    /// domain, not a part), so without this every part re-scans all ~14000 parameters.</summary>
    private readonly Dictionary<string, List<Integra7ParameterSpec>> _prefixCache = new(StringComparer.Ordinal);

    public Integra7Parameters()
        : this(AssetLoader.Open(new Uri("avares://Integra7AuralAlchemist/Assets/parameters.bin")))
    {
    }

    // Test/host seam: load the blob from any stream, bypassing Avalonia's AssetLoader (unavailable in
    // a plain test runner). Takes ownership of the stream and disposes it.
    public Integra7Parameters(Stream blob)
    {
        using (blob)
            _store = ParameterStore.Load(blob);
        for (var i = 0; i < _store.Count; i++) _index[_store.Str(_store.PathIds[i])] = i;
    }

    public Integra7ParameterSpec Lookup(string path)
    {
        if (!_index.TryGetValue(path, out var i))
        {
            Debug.Assert(false, $"Path {path} not found in parameters database.");
            return default;
        }

        return _store.Get(i);
    }

    public int LookupIndex(string path) => _index.TryGetValue(path, out var i) ? i : -1;

    public List<Integra7ParameterSpec> GetParametersFromTo(string firstPar, string endPar)
    {
        var result = new List<Integra7ParameterSpec>();
        int a = LookupIndex(firstPar), b = LookupIndex(endPar);
        Debug.Assert(a != -1);
        Debug.Assert(b != -1);
        Debug.Assert(a <= b);
        for (var i = a; i <= b; i++) result.Add(_store.Get(i));
        return result;
    }

    /// <summary>All parameters whose path starts with <paramref name="prefix"/>, in store order.
    /// The scan runs once per distinct prefix; later calls copy the cached result.</summary>
    public List<Integra7ParameterSpec> GetParametersWithPrefix(string prefix)
    {
        List<Integra7ParameterSpec> cached;
        // Domains are built on one thread today, but the cache is cheap to guard and a torn dictionary
        // would be a miserable bug to chase.
        lock (_prefixCache)
        {
            if (!_prefixCache.TryGetValue(prefix, out cached))
            {
                cached = [];
                for (var i = 0; i < _store.Count; i++)
                    // Ordinal: paths are ASCII identifiers, and the culture-sensitive default is both
                    // slower and the wrong question to ask of them.
                    if (_store.Str(_store.PathIds[i]).StartsWith(prefix, StringComparison.Ordinal))
                        cached.Add(_store.Get(i));
                _prefixCache[prefix] = cached;
            }
        }

        // Hand back a copy so callers keep owning their list, as they did when every call rescanned.
        // Integra7ParameterSpec is a readonly struct, so this shares nothing mutable.
        return new List<Integra7ParameterSpec>(cached);
    }
}
