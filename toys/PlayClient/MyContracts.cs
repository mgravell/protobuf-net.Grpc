﻿using ProtoBuf;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading.Tasks;
namespace Shared_CS
{
    [ServiceContract(Name = "Hyper.Calculator")]
    public interface ICalculator
    {
        ValueTask<MultiplyResult> MultiplyAsync(MultiplyRequest request);
    }

    [DataContract]
    public class MultiplyRequest
    {
        public MultiplyRequest() { }
        public MultiplyRequest(int x, int y) => (X, Y) = (x, y);

        [DataMember(Order = 1)]
        public int X { get; set; }

        [DataMember(Order = 2)]
        public int Y { get; set; }
    }

    [DataContract]
    public class MultiplyResult
    {
        public MultiplyResult() { }
        public MultiplyResult(int result) => Result = result;

        [DataMember(Order = 1)]
        public int Result { get; set; }
    }

    [ServiceContract]
    public interface IDuplex
    {
        IAsyncEnumerable<MultiplyResult> SomeDuplexApiAsync(IAsyncEnumerable<MultiplyRequest> bar, CallContext context = default);
    }

    [DataContract]
    public class BidiStreamingRequest
    {
        [DataMember(Order = 1)]
        public string? Payload { get; set; }
    }

    [DataContract]
    public class BidiStreamingResponse
    {
        [DataMember(Order = 1)]
        public string? Payload { get; set; }
    }

    [Service]
    public interface IBidiStreamingService
    {
        IAsyncEnumerable<BidiStreamingResponse> TestAsync(IAsyncEnumerable<BidiStreamingRequest> request, CallContext options);

        ValueTask<Stream> TestStreamAsync(TestStreamRequest request, CallContext options = default);
    }

    [ProtoContract]
    public class TestStreamRequest
    {
        [ProtoMember(1)]
        public int Seed { get; set; }

        [ProtoMember(2)]
        public long Length { get; set; }
    }
}
