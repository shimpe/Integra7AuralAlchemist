namespace Integra7AuralAlchemist.ParameterGen;

internal static class Program
{
    public static int Main(string[] args)
    {
        string assetsDir = "Src/Assets", output = "Src/Assets/parameters.bin";
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--assets") assetsDir = args[i + 1];
            if (args[i] == "-o") output = args[i + 1];
        }

        var defs = new ParameterDefinitions().Build(assetsDir);
        Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
        Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        // Write to a temp file and move into place so an interrupted generator never leaves a truncated
        // blob with a fresh timestamp (which the incremental build would treat as up-to-date and embed).
        var tmp = output + ".tmp";
        try
        {
            using (var fs = System.IO.File.Create(tmp))
                ParameterBlobWriter.Write(fs, defs);
            System.IO.File.Move(tmp, output, overwrite: true);
        }
        catch
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
            throw;
        }

        System.Console.WriteLine($"Wrote {defs.Count} parameters to {output}");
        return 0;
    }
}
