using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

using CSharpKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using VBKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System;

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
            // check for [Service] or [ServiceContract]
            if (context.SemanticModel.GetDeclaredSymbol(context.Node, token) is not INamedTypeSymbol symbol)
            {
                return Missing;
            }

            var route = GetServiceRoute(symbol);
            if (route is null)
            {
                return Missing;
            }

            Log?.Invoke($"Accepting type: {symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}");

            //TODO build service DTO
            return new Service(symbol.Name,
                route, symbol.ContainingNamespace?.ToDisplayString)
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex.Message);
        }
        return Missing;
    }


    public static string? GetServiceRoute(INamedTypeSymbol type)
    {
        if (type is not null)
        {
            foreach (var attribute in type.GetAttributes())
            {
                var route = GetServiceRoute(attribute);
                if (route is not null) return route;
            }
        }
        return null;
    }

    public static string? GetServiceRoute(AttributeData attribute)
    {
        if (attribute.AttributeClass is
            {
                Name: "ServiceAttribute",
                IsGenericType: false,
                ContainingType: null,
                ContainingNamespace:
                {
                    Name: "Configuration",
                    ContainingNamespace:
                    {
                        Name: "Grpc",
                        ContainingNamespace:
                        {
                            Name: "ProtoBuf",
                            ContainingNamespace.IsGlobalNamespace: true,
                        }
                    }
                }
            })
        { }

        if (attribute.AttributeClass is
            {
                Name: "ServiceContractAttribute",
                IsGenericType: false,
                ContainingType: null,
                ContainingNamespace:
                {
                    Name: "ServiceModel",
                    ContainingNamespace:
                    {
                        Name: "System",
                        ContainingNamespace.IsGlobalNamespace: true,
                    }
                }
            })
        {

        }

        return null;
    }

    // actual thinking
    private void Generate(SourceProductionContext context, (Compilation Left, ImmutableArray<object> Right) tuple)
    {
    }

}
