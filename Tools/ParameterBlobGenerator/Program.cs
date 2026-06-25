namespace Integra7AuralAlchemist.ParameterGen;

internal static class Program
{
    public static int Main(string[] args)
    {
        var defs = new ParameterDefinitions().Build(args.Length > 0 ? args[0] : "Src/Assets");
        Integra7ParameterDatabaseAnalyzer.MarkAllParentParametersAsIsParentTrue(defs);
        Integra7ParameterDatabaseAnalyzer.FillInSecondaryDependencies(defs);
        System.Console.WriteLine($"defs={defs.Count}");
        return 0;
    }
}
