using System.Collections.Immutable;

namespace ProtoBuf.Grpc.BuildTools;

internal record class Service(string Name, string Route, string Namespace, string? OuterType,
    ImmutableArray<Operation> operations, Service.ServiceFlags Flags)
{
    [Flags]
    public enum ServiceFlags
    {
        None = 0,
        Partial = 1,
    }
}
