using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Bootstrapping;

public static class Integra7JsonGzipDumper
{
    /// <summary>
    /// Dumps Integra7ParameterSpec list to a gzip-compressed NDJSON file
    /// and writes a JSON index mapping names to offsets (synchronous version)
    /// </summary>
    public static void Dump(
        string gzPath,
        string indexPath,
        IEnumerable<Integra7ParameterSpec> specs)
    {
        var index = new Dictionary<string, long>();
        long uncompressedOffset = 0;

        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        
        using var fs = new FileStream(
            gzPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var gzip = new GZipStream(fs, CompressionLevel.Optimal);
        using var writer = new StreamWriter(gzip, Encoding.UTF8);
        foreach (var spec in specs)
        {
            var dto = Integra7ParameterSpecDto.FromSpec(spec);
            var json = JsonSerializer.Serialize(dto, options);

            // Store offsets in uncompressed stream
            index[spec.Path] = uncompressedOffset;
            index[spec.Name] = uncompressedOffset;

            writer.WriteLine(json);

            // +1 for newline
            uncompressedOffset += Encoding.UTF8.GetByteCount(json) + 1;
        }

        writer.Flush();

        // Write index file
        var indexJson = JsonSerializer.Serialize(index);
        File.WriteAllText(indexPath, indexJson, Encoding.UTF8);
    }
}
