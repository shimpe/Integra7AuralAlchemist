namespace Integra7AuralAlchemist.Models.Data;

// Shared contract between the build-time generator (Tools/ParameterBlobGenerator) and the runtime
// loader (ParameterStore). Bump Version on any layout change; the loader rejects mismatches.
public static class ParameterBlobFormat
{
    public const uint Magic = 0x49375042; // "I7PB"
    public const int Version = 1;
}
