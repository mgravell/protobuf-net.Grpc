namespace ProtoBuf.Grpc.BuildTools;

internal readonly record struct Operand(string Name, string Route, Operand.OperandKind Kind, Operand.OperandFlags Flags)
{
    [Flags]
    public enum OperandFlags
    {
        None = 0,
        Streaming = 1,
    }

    public enum OperandKind
    {
        Unknown,
        Payload,
        CallContext,
        CancellationToken,
        Metadata,

    }
}
