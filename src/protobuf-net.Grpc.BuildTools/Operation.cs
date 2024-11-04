using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct Operation(LocationSnapshot Location, string Name, string Route, ImmutableArray<Operand> Parameters, Operand Return, Operation.OperationFlags Flags)
{
    public bool IsInvalid => (Flags & OperationFlags.Invalid) != 0;

    internal static ImmutableArray<Operation> Create(INamedTypeSymbol serviceType)
    {
        var members = serviceType.GetMembers();
        if (members.IsDefaultOrEmpty)
        {
            return ImmutableArray<Operation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<Operation>(members.Length);
        foreach (var member in members)
        {
            if (member is IMethodSymbol method)
            {
                var route = GetOperationRoute(method, out var flags);
                if (route is not null)
                {
                    var ret = Operand.Create("", method.ReturnType, method.GetReturnTypeAttributes());
                    var p = Operand.Create(method.Parameters);
                    CascadeInvalid(ref flags, ret, p);
                    builder.Add(new Operation(LocationSnapshot.Create(member), member.Name, route, p, ret, flags));
                }
            }
        }
        return builder.ToImmutable();

        static void CascadeInvalid(ref OperationFlags flags, in Operand ret, in ImmutableArray<Operand> parameters)
        {
            if ((flags & OperationFlags.Invalid) == 0)
            {
                if (ret.IsInvalid)
                {
                    flags |= OperationFlags.Invalid;
                    return;
                }
                foreach (var p in parameters)
                {
                    if (p.IsInvalid)
                    {
                        flags |= OperationFlags.Invalid;
                        return;
                    }
                }
            }
        }
    }

    static string? GetOperationRoute(IMethodSymbol method, out OperationFlags flags)
    {
        flags = OperationFlags.None;
        if (method.IsGenericMethod)
        {
            flags |= OperationFlags.Generic | OperationFlags.Invalid;
        }
        return null;
    }

    [Flags]
    public enum OperationFlags
    {
        None = 0,
        Async = 1,
        Invalid = 2,
        Generic = 3,
    }
}
