using System;
using System.Collections.Generic;
using System.IO;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>Where Init should read its tone from: a file in the library, a bundled asset, or nowhere.
/// Exactly one of the two paths is set when there is a tone at all.</summary>
/// <param name="MarkWasStale">True when the user had marked a library entry for this engine and it is no
/// longer there. The bundled tone is still used, but the command says so -- silently loading a different
/// sound than the one that was marked is how a user stops trusting the feature.</param>
public sealed record InitToneSource(string? FilePath, string? AssetUri, bool MarkWasStale)
{
    public bool HasTone => FilePath is not null || AssetUri is not null;
}

/// <summary>Which tone Init loads for an engine.
///
/// Pure by construction: existence is asked of the caller through two predicates rather than of the file
/// system and Avalonia's asset loader directly, which is what lets every branch be tested. The view model
/// passes <c>File.Exists</c> and an asset-loader probe.</summary>
public static class InitToneResolution
{
    /// <summary>Where a build's own init tone for an engine lives. Named by tone type, so the five files
    /// are PCMS.json, PCMD.json, SN-S.json, SN-A.json and SN-D.json.</summary>
    public static string AssetUriFor(string toneType) =>
        $"avares://Integra7AuralAlchemist/Assets/InitTones/{toneType}.json";

    /// <param name="marks">The init-tone marks from the settings file: tone type to a file name relative
    /// to <paramref name="libraryFolder"/>.</param>
    public static InitToneSource Resolve(IReadOnlyDictionary<string, string> marks, string libraryFolder,
        string toneType, Func<string, bool> fileExists, Func<string, bool> assetExists)
    {
        var marked = marks.TryGetValue(toneType, out var name) && !string.IsNullOrWhiteSpace(name)
            ? Path.Combine(libraryFolder, name)
            : null;

        if (marked is not null && fileExists(marked))
            return new InitToneSource(marked, null, MarkWasStale: false);

        var asset = AssetUriFor(toneType);
        return new InitToneSource(null, assetExists(asset) ? asset : null,
            MarkWasStale: marked is not null);
    }
}
