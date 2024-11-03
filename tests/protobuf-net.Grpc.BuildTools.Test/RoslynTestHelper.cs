using Grpc.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ProtoBuf.Grpc.Configuration;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.ServiceModel;

namespace ProtoBuf.Grpc.BuildTools;

internal static class RoslynTestHelpers

{
    public static KeyValuePair<string, string> InterceptorsPreviewNamespacePair => new("InterceptorsPreviewNamespaces", ServiceGenerator.CodegenNamespace);

    internal static readonly CSharpParseOptions ParseOptionsLatestLangVer = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Latest)
        .WithPreprocessorSymbols(new string[] {
#if NETFRAMEWORK
        "NETFRAMEWORK",
#endif
#if NET40_OR_GREATER
        "NET40_OR_GREATER",
#endif
#if NET48_OR_GREATER
        "NET48_OR_GREATER",
#endif
#if NET6_0_OR_GREATER
        "NET6_0_OR_GREATER",
#endif
#if NET7_0_OR_GREATER
        "NET7_0_OR_GREATER",
#endif
#if DEBUG
        "DEBUG",
#endif
#if RELEASE
        "RELEASE",
#endif
    })
    .WithFeatures([ InterceptorsPreviewNamespacePair ]);

    public static Compilation CreateCompilation(string source, string name, string fileName)
       => CSharpCompilation.Create(name,
           syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, ParseOptionsLatestLangVer).WithFilePath(fileName) },
           references: new[] {
                   MetadataReference.CreateFromFile(typeof(Binder).Assembly.Location),
#if !NET48
                   MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                   MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
                   MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
#endif
                   MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ValueTask<int>).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(Component).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ImmutableList<int>).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ImmutableArray<int>).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<int>).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(Span<int>).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(IgnoreDataMemberAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(DynamicAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ChannelBase).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ServiceAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ServiceContractAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(ProtoContractAttribute).Assembly.Location),
                   MetadataReference.CreateFromFile(typeof(DataContractAttribute).Assembly.Location),
           },
           options: new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true));
}
