using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Integra7AuralAlchemist.Models.Bootstrapping;
using Integra7AuralAlchemist.Models.Data;

namespace Integra7AuralAlchemist.Models.Services;  

public sealed class Integra7GzipJsonRepository : IAsyncDisposable
{
    private JsonSerializerOptions _options = new JsonSerializerOptions
    {
        WriteIndented = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };
    private readonly string _gzPath;
    private readonly Dictionary<string, long> _index;
    private readonly LruCache<string, Integra7ParameterSpec> _cache;

    public Integra7GzipJsonRepository(
        string gzPath,
        string indexPath,
        int cacheSize = 1024)
    {
        _gzPath = gzPath;

        _index = JsonSerializer.Deserialize<Dictionary<string, long>>(
                     File.ReadAllText(indexPath))!;

        _cache = new LruCache<string, Integra7ParameterSpec>(cacheSize);
    }

    public async Task<Integra7ParameterSpec?> GetAsync(string pathOrName)
    {
        if (_cache.TryGet(pathOrName, out var cached))
            return cached;

        if (!_index.TryGetValue(pathOrName, out var targetOffset))
            return null;

        await using var fs = new FileStream(
            _gzPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        long currentOffset = 0;
        
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                return null;

            long lineBytes = Encoding.UTF8.GetByteCount(line) + 1;

            if (currentOffset == targetOffset)
            {
                var dto = JsonSerializer.Deserialize<Integra7ParameterSpecDto>(line, _options);
                if (dto == null) return null;

                var spec = dto.ToSpec();

                // cache by both keys
                _cache.Add(spec.Path, spec);
                _cache.Add(spec.Name, spec);

                return spec;
            }

            currentOffset += lineBytes;
        }
    }

    public async IAsyncEnumerable<Integra7ParameterSpec> EnumerateAsync()
    {
        await using var fs = new FileStream(
            _gzPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                yield break;

            var dto = JsonSerializer.Deserialize<Integra7ParameterSpecDto>(line, _options);
            if (dto != null)
                yield return dto.ToSpec();
        }
    }
    
    public async Task<List<Integra7ParameterSpec>> GetRangeAsync(
        string firstPar,
        string endPar)
    {
        var result = new List<Integra7ParameterSpec>();
        bool collecting = false;

        await using var fs = new FileStream(
            _gzPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            8192,
            true);

        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                break;

            var dto = JsonSerializer.Deserialize<Integra7ParameterSpecDto>(line, _options);
            if (dto == null)
                continue;

            var spec = dto.ToSpec();

            // cache hot specs opportunistically
            _cache.Add(spec.Path, spec);
            _cache.Add(spec.Name, spec);

            if (!collecting)
            {
                if (spec.Path == firstPar)
                {
                    collecting = true;
                    result.Add(spec);

                    if (spec.Path == endPar)
                        break;
                }
            }
            else
            {
                result.Add(spec);

                if (spec.Path == endPar)
                    break;
            }
        }

        return result;
    }
    public async Task<List<Integra7ParameterSpec>> GetRangeByPrefixAsync(
        string prefix)
    {
        var result = new List<Integra7ParameterSpec>();
        bool collecting = false;

        await using var fs = new FileStream(
            _gzPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            8192,
            true);

        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null)
                break;

            var dto = JsonSerializer.Deserialize<Integra7ParameterSpecDto>(line, _options);
            if (dto == null)
                continue;

            var spec = dto.ToSpec();

            // warm cache
            _cache.Add(spec.Path, spec);
            _cache.Add(spec.Name, spec);

            if (!collecting)
            {
                if (spec.Path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    collecting = true;
                    result.Add(spec);
                }
            }
            else
            {
                if (spec.Path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(spec);
                }
                else
                {
                    // prefix block ended → early exit
                    break;
                }
            }
        }

        return result;
    }
    
    public List<Integra7ParameterSpec> GetRangeByPrefix(string prefix)
    {
        var result = new List<Integra7ParameterSpec>();
        bool collecting = false;

        using var fs = new FileStream(
            _gzPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        while (true)
        {
            var line = reader.ReadLine();
            if (line == null)
                break;

            var dto = JsonSerializer.Deserialize<Integra7ParameterSpecDto>(line, _options);
            if (dto == null)
                continue;

            var spec = dto.ToSpec();

            // warm the cache
            _cache.Add(spec.Path, spec);
            _cache.Add(spec.Name, spec);

            if (!collecting)
            {
                if (spec.Path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    collecting = true;
                    result.Add(spec);
                }
            }
            else
            {
                if (spec.Path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(spec);
                }
                else
                {
                    // prefix block ended → early exit
                    break;
                }
            }
        }

        return result;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
