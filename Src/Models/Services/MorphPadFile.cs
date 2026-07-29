using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A saved morph pad: which engine it is locked to, the patch on each corner, and where the
/// point was left.</summary>
/// <param name="CornerFiles">File names <em>relative to the library folder</em>, not full paths, for the
/// reason the init-tone marks are relative: the library folder is a setting the user can change, and a
/// pad should follow it.</param>
public sealed record MorphPad(string ToneType, IReadOnlyList<string> CornerFiles, double X, double Y);

/// <summary>Reading and writing <see cref="MorphPad"/>. Indented, because somebody will open one in an
/// editor, and written atomically for the reason the settings file is: a truncated file is not JSON, and
/// losing a seven-corner pad to a half-finished write would be a miserable way to lose one.</summary>
public static class MorphPadFile
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Where pads live: a <c>Pads</c> folder beside the library rather than inside it. Beside,
    /// because the library is a folder of snapshots and a pad is not one -- a pad sitting in it would be a
    /// file the listing silently skips, which is the shape of a bug rather than of a design.
    ///
    /// A library folder that is a drive root has nothing beside it, so the pads go under it instead. That
    /// is a strange place to keep a library and a worse place to throw.</summary>
    public static string FolderBeside(string libraryFolder)
    {
        // Trimmed first: GetDirectoryName of a path ending in a separator answers the path itself, so
        // "…/Library/" would put the pads inside the library rather than beside it.
        var parent = Path.GetDirectoryName(
            libraryFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return Path.Combine(string.IsNullOrEmpty(parent) ? libraryFolder : parent, "Pads");
    }

    /// <summary>How a corner's file is written into a pad: its bare name when it sits in the library
    /// folder, its whole path when it does not.
    ///
    /// The bare name is the point -- see <see cref="MorphPad.CornerFiles"/> -- but the corner picker is a
    /// file dialog and a user can walk it anywhere. A pad that silently forgot such a corner would be
    /// worse than one carrying a path that stops working if the file moves.</summary>
    public static string RelativeName(string libraryFolder, string filePath)
    {
        if (libraryFolder.Length == 0) return filePath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return directory is not null && string.Equals(Trimmed(directory), Trimmed(libraryFolder),
            StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(filePath)
            : filePath;

        static string Trimmed(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    /// <summary>The other direction: a bare name is looked for in the library folder, a whole path is
    /// taken as it stands.</summary>
    public static string Resolve(string libraryFolder, string name) =>
        Path.IsPathRooted(name) ? name : Path.Combine(libraryFolder, name);

    public static void Save(string path, MorphPad pad)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(pad, Options));
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // The half-written file is the thing the atomic write exists to avoid leaving behind, so it
            // goes even though the failure it belongs to is being rethrown.
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (Exception cleanup)
            {
                Serilog.Log.Warning(cleanup, "Could not remove {Path}", tempPath);
            }

            throw;
        }
    }

    /// <summary>Throws <see cref="SnapshotFormatException"/> for anything that is not a pad, so the
    /// caller has one exception type to show the user -- the same contract the snapshot reader has.</summary>
    public static MorphPad Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<MorphPad>(File.ReadAllText(path), Options)
                   ?? throw new SnapshotFormatException($"\"{Path.GetFileName(path)}\" holds no pad.");
        }
        catch (JsonException e)
        {
            throw new SnapshotFormatException(
                $"\"{Path.GetFileName(path)}\" is not a morph pad file.", e);
        }
    }
}
