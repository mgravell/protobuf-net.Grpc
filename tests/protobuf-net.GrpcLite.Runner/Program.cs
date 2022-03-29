﻿using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using ProtoBuf.Grpc.Lite;
using protobuf_net.GrpcLite.Test;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static FooService;

static class Program
{
    static readonly Dictionary<string, (string unary, string clientStreamingBuffered, string clientStreamingNonBuffered, string serverStreamingBuffered, string serverStreamingNonBuffered, string duplex)> timings = new();

    [Flags]
    public enum Tests
    {
        None = 0,
        NamedPipe = 1 << 0,
        NamedPipeTls = 1 << 1,
        NamedPipePassThru = 1 << 2,
        NamedPipeMerge = 1 << 3,
        Tcp = 1 << 4,
        TcpTls = 1 << 5,
        TcpKestrel = 1 << 6,
        Unmanaged = 1 << 7,
        Local = 1 << 8,
        Managed = 1 << 9,
        ManagedTls = 1 << 10,
    }
    static async Task Main(string[] args)
    {
        RemoteCertificateValidationCallback trustAny = delegate { return true; };
        Tests tests;
        if (args.Length == 0)
        {
            // reasonable defaults
            tests = Tests.NamedPipe | Tests.NamedPipeTls | Tests.Local | Tests.Tcp | Tests.TcpTls
                | Tests.Unmanaged;
#if !NET472
            tests |= Tests.Managed | Tests.ManagedTls;
#endif
        }
        else
        {
            tests = Tests.None;
            foreach (var arg in args)
            {
                if (Enum.TryParse(arg, true, out Tests tmp))
                    tests |= tmp;
            }
        }
        Console.WriteLine($"Running tests: {tests}");

        bool ShouldRun(Tests test)
            => (tests & test) != 0;

        if (ShouldRun(Tests.TcpKestrel))
        {
            using var pipeServer = await ConnectionFactory.ConnectSocket(new IPEndPoint(IPAddress.Loopback, 10044)).AsFrames().CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(pipeServer, Tests.TcpKestrel);
        }
        if (ShouldRun(Tests.NamedPipeMerge))
        {
            using var namedPipe = await ConnectionFactory.ConnectNamedPipe("grpctest_buffer", logger: ConsoleLogger.Debug).AsFrames().CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(namedPipe, Tests.NamedPipeMerge);
        }
        if (ShouldRun(Tests.NamedPipePassThru))
        {
            using var namedPipePassThru = await ConnectionFactory.ConnectNamedPipe("grpctest_passthru", logger: ConsoleLogger.Debug).AsFrames(outputBufferSize: 0).CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(namedPipePassThru, Tests.NamedPipePassThru);
        }

        if (ShouldRun(Tests.NamedPipeMerge))
        {
            using var namedPipeMerge = await ConnectionFactory.ConnectNamedPipe("grpctest_merge").AsFrames(true).CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(namedPipeMerge, Tests.NamedPipeMerge);
        }
        if (ShouldRun(Tests.Tcp))
        {
            using var tcp = await ConnectionFactory.ConnectSocket(new IPEndPoint(IPAddress.Loopback, 10042)).AsFrames().CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(tcp, Tests.Tcp);
        }
#if NET472
        ServicePointManager.ServerCertificateValidationCallback = trustAny;
#endif
        if (ShouldRun(Tests.TcpTls))
        {
            using var tcpTls = await ConnectionFactory.ConnectSocket(new IPEndPoint(IPAddress.Loopback, 10043))
    .WithTls().AuthenticateAsClient("mytestserver"
#if !NET472
                    , trustAny
#endif
                    ).AsFrames().CreateChannelAsync(TimeSpan.FromSeconds(5));
            await Run(tcpTls, Tests.TcpTls);
        }

        if (ShouldRun(Tests.NamedPipeTls))
        {
            using var namedPipeTls = await ConnectionFactory.ConnectNamedPipe("grpctest_tls").WithTls()
    .AuthenticateAsClient("mytestserver"
#if !NET472
                    , trustAny
#endif
                    ).AsFrames().CreateChannelAsync(TimeSpan.FromSeconds(50));
            await Run(namedPipeTls, Tests.NamedPipeTls);
        }

        if (ShouldRun(Tests.Managed))
        {
            using var managedHttp = GrpcChannel.ForAddress("http://localhost:5074");
            await Run(managedHttp, Tests.Managed);
        }
        if (ShouldRun(Tests.ManagedTls))
        {
            using (var managedHttps = GrpcChannel.ForAddress("https://localhost:7074"))
            {
                await Run(managedHttps, Tests.ManagedTls);
            }
        }

        if (ShouldRun(Tests.Unmanaged))
        {
            var unmanagedHttp = new Channel("localhost", 5074, ChannelCredentials.Insecure);
            await Run(unmanagedHttp, Tests.Unmanaged);
            await unmanagedHttp.ShutdownAsync();
        }

        if (ShouldRun(Tests.Local))
        {
            using var localServer = new LiteServer();
            localServer.Bind<MyService>();
            using var local = localServer.CreateLocalClient();
            await Run(local, Tests.Local);
        }


        Console.WriteLine();
        Console.WriteLine("| Scenario | Unary | Client-Streaming (b) | Client-Streaming (n) | Server-Streaming (b) | Server-Streaming (n) | Duplex |");
        Console.WriteLine("| -------- | ----- | -------------------- | -------------------- | -------------------- | -------------------- | ------ |");
        foreach (var pair in timings.OrderBy(x => x.Key))
        {
            var scenario = pair.Key;
            var data = pair.Value;
            Console.WriteLine($"| {scenario} | {data.unary} | {data.clientStreamingBuffered} | {data.clientStreamingNonBuffered} | {data.serverStreamingBuffered} | {data.serverStreamingNonBuffered} | {data.duplex} |");
        }

    }
    async static Task Run(ChannelBase channel, Tests test, int repeatCount = 10)
    {
        try
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var options = new CallOptions(cancellationToken: cts.Token);

            var invoker = channel.CreateCallInvoker();
            Console.WriteLine($"Connecting to {channel.Target} ({test}, {invoker.GetType().Name})...");
            var client = new FooServiceClient(invoker);

            using (var call = client.UnaryAsync(new FooRequest { Value = 42 }, options))
            {
                var result = await call.ResponseAsync;
                if (result?.Value != 42) throw new InvalidOperationException("Incorrect response received: " + result);
                Console.WriteLine();
            }

            long unary = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                const int OPCOUNT = 10000;
                for (int i = 0; i < OPCOUNT; i++)
                {
                    using var call = client.UnaryAsync(new FooRequest { Value = i }, options);
                    var result = await call.ResponseAsync;

                    if (result?.Value != i) throw new InvalidOperationException("Incorrect response received: " + result);
                }
                unary += ShowTiming(nameof(client.UnaryAsync), watch, OPCOUNT);
            }
            Console.WriteLine();

            long clientStreamingBuffered = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                using var call = client.ClientStreaming(options);
                const int OPCOUNT = 50000;
                int sum = 0;
                call.RequestStream.WriteOptions = MyService.Buffered;
                for (int i = 0; i < OPCOUNT; i++)
                {
                    await call.RequestStream.WriteAsync(new FooRequest { Value = i });
                    sum += i;
                }
                await call.RequestStream.CompleteAsync();
                var result = await call.ResponseAsync;
                if (result?.Value != sum) throw new InvalidOperationException("Incorrect response received: " + result);
                clientStreamingBuffered += ShowTiming(nameof(client.ClientStreaming) + " b", watch, OPCOUNT);
            }
            Console.WriteLine();

            long clientStreamingNonBuffered = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                using var call = client.ClientStreaming(options);
                const int OPCOUNT = 50000;
                int sum = 0;
                call.RequestStream.WriteOptions = MyService.NonBuffered;
                for (int i = 0; i < OPCOUNT; i++)
                {
                    await call.RequestStream.WriteAsync(new FooRequest { Value = i });
                    sum += i;
                }
                await call.RequestStream.CompleteAsync();
                var result = await call.ResponseAsync;
                if (result?.Value != sum) throw new InvalidOperationException("Incorrect response received: " + result);
                clientStreamingNonBuffered += ShowTiming(nameof(client.ClientStreaming) + " nb", watch, OPCOUNT);
            }
            Console.WriteLine();

            long serverStreamingBuffered = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                const int OPCOUNT = 50000;
                using var call = client.ServerStreaming(new FooRequest { Value = OPCOUNT }, options);
                int count = 0;
                while (await call.ResponseStream.MoveNext())
                {
                    var result = call.ResponseStream.Current;
                    if (result?.Value != count) throw new InvalidOperationException("Incorrect response received: " + result);
                    count++;
                }
                if (count != OPCOUNT) throw new InvalidOperationException("Incorrect response count received: " + count);
                serverStreamingBuffered += ShowTiming(nameof(client.ServerStreaming) + " b", watch, OPCOUNT);
            }
            Console.WriteLine();

            long serverStreamingNonBuffered = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                const int OPCOUNT = 50000;
                using var call = client.ServerStreaming(new FooRequest { Value = -OPCOUNT }, options);
                int count = 0;
                while (await call.ResponseStream.MoveNext())
                {
                    var result = call.ResponseStream.Current;
                    if (result?.Value != count) throw new InvalidOperationException("Incorrect response received: " + result);
                    count++;
                }
                if (count != OPCOUNT) throw new InvalidOperationException("Incorrect response count received: " + count);
                serverStreamingNonBuffered += ShowTiming(nameof(client.ServerStreaming) + " nb", watch, OPCOUNT);
            }
            Console.WriteLine();

            long duplex = 0;
            for (int j = 0; j < repeatCount; j++)
            {
                var watch = Stopwatch.StartNew();
                const int OPCOUNT = 25000;
                using var call = client.Duplex(options);

                for (int i = 0; i < OPCOUNT; i++)
                {
                    await call.RequestStream.WriteAsync(new FooRequest { Value = i });
                    if (!await call.ResponseStream.MoveNext()) throw new InvalidOperationException("Duplex stream terminated early");
                    var result = call.ResponseStream.Current;
                    if (result?.Value != i) throw new InvalidOperationException("Incorrect response received: " + result);
                }
                await call.RequestStream.CompleteAsync();
                if (await call.ResponseStream.MoveNext()) throw new InvalidOperationException("Duplex stream ran over");
                duplex += ShowTiming(nameof(client.Duplex), watch, OPCOUNT);
            }
            Console.WriteLine();
            // store the average nanos-per-op
            timings.Add(test.ToString(), (
                AutoScale(unary / repeatCount, true),
                AutoScale(clientStreamingBuffered / repeatCount, true),
                AutoScale(clientStreamingNonBuffered / repeatCount, true),
                AutoScale(serverStreamingBuffered / repeatCount, true),
                AutoScale(serverStreamingNonBuffered / repeatCount, true),
                AutoScale(duplex / repeatCount, true)
            ));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{channel.Target}]: {ex.Message}");
        }
        finally
        {
            try { await channel.ShutdownAsync(); }
            catch { }
        }

        static long ShowTiming(string label, Stopwatch watch, int operations)
        {
            watch.Stop();
            var nanos = (watch.ElapsedTicks * 1_000_000_000) / Stopwatch.Frequency;
            Console.WriteLine($"{label} ×{operations}: {AutoScale(nanos)}, {AutoScale(nanos / operations)}/op");
            return nanos / operations;
        }
        static string AutoScale(long nanos, bool forceNanos = false)
        {
            long qty = nanos;
            if (forceNanos) return $"{qty:###,###,##0}ns";
            if (qty < 10000) return $"{qty:#,##0}ns";
            qty /= 1000;
            if (qty < 10000) return $"{qty:#,##0}μs";
            qty /= 1000;
            if (qty < 10000) return $"{qty:#,##0}ms";

            return TimeSpan.FromMilliseconds(qty).ToString();
        }
    }

}