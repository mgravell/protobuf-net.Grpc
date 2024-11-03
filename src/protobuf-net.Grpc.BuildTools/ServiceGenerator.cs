using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

using CSharpKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using VBKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace ProtoBuf.Grpc.BuildTools;

[Generator(LanguageNames.CSharp, LanguageNames.VisualBasic)]
[DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
public sealed class ServiceGenerator : DiagnosticAnalyzer, IIncrementalGenerator
{
    public const string CodegenNamespace = "ProtoBuf.Grpc.AOT";
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray<DiagnosticDescriptor>.Empty;

    public event Action<string>? Log;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1026:Enable concurrent execution", Justification = "Done in release")]
    public override void Initialize(AnalysisContext context)
    {
#if !DEBUG
        context.EnableConcurrentExecution();
#endif
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var nodes = context.SyntaxProvider.CreateSyntaxProvider(PreFilter, Parse)
            .Where(x => x is not null)
            .Select((x, _) => x!);
        var combined = context.CompilationProvider.Combine(nodes.Collect());
        context.RegisterImplementationSourceOutput(combined, Generate);
    }

    // fail-fast check to exclude irrelevant nodes
    private bool PreFilter(SyntaxNode node, CancellationToken token) => (node.IsKind(CSharpKind.InterfaceDeclaration) && node is InterfaceDeclarationSyntax)
            || (node.IsKind(VBKind.InterfaceStatement) && node is InterfaceStatementSyntax);

    private static readonly object Missing = null!;

    // minimal scan and validate of the object to a model that is detached from the Roslyn core (for GC reasons)
    private object Parse(GeneratorSyntaxContext context, CancellationToken token)
    {
        try
        {
            var symbol = context.SemanticModel.GetDeclaredSymbol(context.Node, token);
            if (symbol is null) return Missing;

            // check for [Service] or [ServiceContract]
            bool isService = false;
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.IsType("ProtoBuf", "Grpc", "Configuration", "ServiceAttribute")
                    || attribute.IsType("System", "ServiceModel", "ServiceContractAttribute"))
                {
                    isService = true;
                    break;
                }
            }

            if (!isService)
            {
                return Missing;
            }

            Log?.Invoke($"Accepting type: {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");

            //TODO build service DTO
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex.Message);
        }
        return Missing;
    }

    // actual thinking
    private void Generate(SourceProductionContext context, (Compilation Left, ImmutableArray<object> Right) tuple)
    {
    }

}
internal static class TypeHelpers
{
    public static bool IsType(this AttributeData attrib, string ns0, string ns1, string name)
        => attrib?.AttributeClass is { } type && IsType(type, ns0, ns1, name);

    public static bool IsType(this AttributeData attrib, string ns0, string ns1, string ns2, string name)
        => attrib?.AttributeClass is { } type && IsType(type, ns0, ns1, ns2, name);
    public static bool IsType(this INamedTypeSymbol type, string ns0, string ns1, string name)
    {
        if (type is not null && type.Name == name && !type.IsGenericType
            && type.ContainingType is null && type.ContainingNamespace is { } ns && ns.Name == ns1)
        {
            ns = ns.ContainingNamespace;
            if (ns is not null && ns.Name == ns0 && ns.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
        }
        return false;
    }
    public static bool IsType(this INamedTypeSymbol type, string ns0, string ns1, string ns2, string name)
    {
        if (type is not null && type.Name == name && !type.IsGenericType
            && type.ContainingType is null && type.ContainingNamespace is { } ns && ns.Name == ns2)
        {
            ns = ns.ContainingNamespace;
            if (ns is not null && ns.Name == ns1)
            {
                ns = ns.ContainingNamespace;
                if (ns is not null && ns.Name == ns0 && ns.ContainingNamespace.IsGlobalNamespace)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
