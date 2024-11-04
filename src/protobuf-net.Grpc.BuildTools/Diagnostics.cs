using Microsoft.CodeAnalysis;

namespace ProtoBuf.Grpc.BuildTools;

[System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008:Enable analyzer release tracking", Justification = "Overkill here")]
internal static class Diagnostics
{
    private const string CategoryGrpc = "gRPC", RulesRoot = "https://protobuf-net.github.io/protobuf-net.Grpc/rules/";
    public static readonly DiagnosticDescriptor InvalidService = Error("PBNGRPC001", "Invalid service", "The service '{0}' is invalid and cannot be used");

    private static DiagnosticDescriptor Error(string id, string title, string messageFormat, string category = CategoryGrpc) =>
        Create(id, title, messageFormat, category, DiagnosticSeverity.Error);
    private static DiagnosticDescriptor Create(string id, string title, string messageFormat, string category, DiagnosticSeverity severity) =>
    new(id, title,
        messageFormat, category, severity, true, helpLinkUri: RulesRoot + id);
}
