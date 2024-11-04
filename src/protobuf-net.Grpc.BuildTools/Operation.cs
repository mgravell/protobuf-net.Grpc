using System.Collections.Immutable;

namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct Operation(string Name, string Route, ImmutableArray<Operand> Parameters, Operand Return, Operation.OperationFlags Flags)
{
    [Flags]
    public enum OperationFlags
    {
        None = 0,
        Async = 1,
    }
}
