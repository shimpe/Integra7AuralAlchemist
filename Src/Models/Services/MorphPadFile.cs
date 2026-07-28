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
