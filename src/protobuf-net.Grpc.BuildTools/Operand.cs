using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct Operand(string Name, Operand.OperandKind Kind, Operand.OperandFlags Flags)
{
    public bool IsInvalid => (Flags & OperandFlags.Invalid) != 0;

    internal static Operand Create(string name, ITypeSymbol type, ImmutableArray<AttributeData> attributes)
    {
        var flags = OperandFlags.None;
        switch (type.SpecialType)
        {
            case SpecialType.System_Void:
                return new(name, OperandKind.Empty, flags);
        }

        if (type is INamedTypeSymbol {  IsGenericType: true })
        {

        }

        return new(name, OperandKind.Payload, flags);
    }

    internal static ImmutableArray<Operand> Create(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return ImmutableArray<Operand>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<Operand>(parameters.Length);
        foreach (var p in parameters)
        {
            builder.Add(Create(p.Name, p.Type, p.GetAttributes()));
        }
        return builder.ToImmutable();
    }

    [Flags]
    public enum OperandFlags
    {
        None = 0,
        Invalid = 1 << 0,
        Streaming = 1 << 1,
        Task = 1 << 2,
        ValueTask = 1 << 3,
        SystemIOStream = 1 << 4,
    }

    public enum OperandKind
    {
        Unknown,
        Empty,
        Payload,
        CallContext,
        CancellationToken,
        Metadata,
    }
}
