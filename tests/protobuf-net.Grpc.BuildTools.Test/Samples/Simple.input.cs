namespace Protobuf.Grpc.BuildTools.Test.Samples;

using ProtoBuf.Grpc.Configuration;

interface INotAService
{

}

[Service]
interface ISomeService
{
    void Foo(int i);

    void Bar() => Foo(42);

    static void WriteWorld() { }
}

interface ISomeOtherService : IGrpcService
{
    void Foo(int i);

    void Bar() => Foo(42);

    static void WriteWorld() { }
}