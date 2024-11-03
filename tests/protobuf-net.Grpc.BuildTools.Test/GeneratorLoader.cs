using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ProtoBuf.Grpc.BuildTools.Test;

public class GeneratorLoader(ITestOutputHelper log)
{
    public static IEnumerable<object[]> GetFiles() =>
        from path in Directory.GetFiles("Samples", "*.cs", SearchOption.AllDirectories)
        where path.EndsWith(".input.cs", StringComparison.OrdinalIgnoreCase)
        select new object[] { path };

    [Theory, MemberData(nameof(GetFiles))]
    public void Test(string path)
    {
        var sourceText = File.ReadAllText(path);
#if NET48   // lots of deltas
        var outputCodePath = Regex.Replace(path, @"\.input\.cs$", ".output.netfx.cs", RegexOptions.IgnoreCase);
#else
        var outputCodePath = Regex.Replace(path, @"\.input\.cs$", ".output.cs", RegexOptions.IgnoreCase);
#endif
        var outputBuildPath = Path.ChangeExtension(outputCodePath, "txt");

        var expectedCode = File.Exists(outputCodePath) ? File.ReadAllText(outputCodePath) : "";
        var expectedBuildOutput = File.Exists(outputBuildPath) ? File.ReadAllText(outputBuildPath) : "";

        var sb = new StringBuilder();
        var result = Execute<ServiceGenerator>(sourceText, sb, fileName: path, initializer: g =>
        {
            g.Log += message => log.WriteLine(message);
        });

        var results = Assert.Single(result.Result.Results);
        string actualCode = results.GeneratedSources.Any() ? results.GeneratedSources.Single().SourceText?.ToString() ?? "" : "";

        var buildOutput = sb.ToString();
        try // automatically overwrite test output, for git tracking
        {
            if (GetOriginCodeLocation() is string originFile
                && Path.GetDirectoryName(originFile) is string originFolder)
            {
                outputCodePath = Path.Combine(originFolder, outputCodePath);
                outputBuildPath = Path.ChangeExtension(outputCodePath, "txt");
                if (string.IsNullOrWhiteSpace(buildOutput))
                {
                    try { File.Delete(outputBuildPath); } catch { }
                }
                else
                {
                    File.WriteAllText(outputBuildPath, buildOutput);
                }
                if (string.IsNullOrWhiteSpace(actualCode))
                {
                    try { File.Delete(outputCodePath); } catch { }
                }
                else
                {
                    File.WriteAllText(outputCodePath, actualCode);
                }
            }
        }
        catch (Exception ex)
        {
            log.WriteLine(ex.Message);
        }
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(expectedCode.Trim(), actualCode.Trim(), ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);
        Assert.Equal(expectedBuildOutput.Trim(), buildOutput.Trim(), ignoreLineEndingDifferences: true, ignoreWhiteSpaceDifferences: true);

        static string? GetOriginCodeLocation([CallerFilePath] string? path = null) => path;
    }

    protected (Compilation? Compilation, GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics, int ErrorCount) Execute<T>(string source,
     StringBuilder? diagnosticsTo = null,
     [CallerMemberName] string? name = null,
     string? fileName = null,
     Action<T>? initializer = null
     ) where T : class, IIncrementalGenerator, new()
    {
        void OutputDiagnostic(Diagnostic d)
        {
            Output("", true);
            var loc = d.Location.GetMappedLineSpan();
            Output($"{d.Severity} {d.Id} {loc.Path} L{loc.StartLinePosition.Line + 1} C{loc.StartLinePosition.Character + 1}");
            Output(d.GetMessage(CultureInfo.InvariantCulture));
        }
        void Output(string message, bool force = false)
        {
            if (force || !string.IsNullOrWhiteSpace(message))
            {
                log.WriteLine(message);
                diagnosticsTo?.AppendLine(message.Replace('\\', '/')); // need to normalize paths
            }
        }
        // Create the 'input' compilation that the generator will act on
        if (string.IsNullOrWhiteSpace(name)) name = "compilation";
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "input.cs";
        Compilation inputCompilation = RoslynTestHelpers.CreateCompilation(source, name!, fileName!);

        // directly create an instance of the generator
        // (Note: in the compiler this is loaded from an assembly, and created via reflection at runtime)
        T generator = new();
        initializer?.Invoke(generator);

        ShowDiagnostics("Input code", inputCompilation, diagnosticsTo, "CS8795", "CS1701", "CS1702");

        // Create the driver that will control the generation, passing in our generator
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: RoslynTestHelpers.ParseOptionsLatestLangVer);

        // Run the generation pass
        // (Note: the generator driver itself is immutable, and all calls return an updated version of the driver that you should use for subsequent calls)
        driver = driver.RunGeneratorsAndUpdateCompilation(inputCompilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();

        foreach (var result in runResult.Results)
        {
            if (result.Exception is not null) throw result.Exception;
        }

        var dn = Normalize(diagnostics, Array.Empty<string>());
        if (dn.Any())
        {
            Output($"Generator produced {dn.Count} diagnostics:");
            foreach (var d in dn)
            {
                OutputDiagnostic(d);
            }
        }

        var errorCount = ShowDiagnostics("Output code", outputCompilation, diagnosticsTo, "CS1701", "CS1702");
        return (outputCompilation, runResult, diagnostics, errorCount);
    }

    int ShowDiagnostics(string caption, Compilation compilation, StringBuilder? diagnosticsTo, params string[] ignore)
    {
        if (diagnosticsTo is null) return 0; // nothing useful to do!
        void Output(string message, bool force = false)
        {
            if (force || !string.IsNullOrWhiteSpace(message))
            {
                log.WriteLine(message);
                diagnosticsTo?.AppendLine(message.Replace('\\', '/')); // need to normalize paths
            }
        }
        int errorCountTotal = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var rawDiagnostics = compilation.GetSemanticModel(tree).GetDiagnostics();
            var diagnostics = Normalize(rawDiagnostics, ignore);
            errorCountTotal += rawDiagnostics.Count(x => x.Severity == DiagnosticSeverity.Error);

            if (diagnostics.Any())
            {
                Output($"{caption} has {diagnostics.Count} diagnostics from '{tree.FilePath}':");
                foreach (var d in diagnostics)
                {
                    OutputDiagnostic(d);
                }
            }
        }
        return errorCountTotal;

        void OutputDiagnostic(Diagnostic d)
        {
            Output("", true);
            var loc = d.Location.GetMappedLineSpan();
            Output($"{d.Severity} {d.Id} {loc.Path} L{loc.StartLinePosition.Line + 1} C{loc.StartLinePosition.Character + 1}");
            Output(d.GetMessage(CultureInfo.InvariantCulture));
        }
    }

    static List<Diagnostic> Normalize(ImmutableArray<Diagnostic> diagnostics, string[] ignore) => (
        from d in diagnostics
        where !ignore.Contains(d.Id)
        let loc = d.Location
        orderby loc.SourceTree?.FilePath, loc.SourceSpan.Start, d.Id, d.ToString()
        select d).ToList();
}
