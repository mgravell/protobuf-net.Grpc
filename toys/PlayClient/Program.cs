﻿using Grpc.Core;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Client;
using Shared_CS;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable IDE0051 // unused members

namespace PlayClient
{
    static class Program
    {
        static async Task Main()
        {
            await TestChannel();
#if HTTPCLIENT
            await TestHttpClient();
#endif
        }

        static async Task TestCalculator(ICalculator calculator, [CallerMemberName] string? caller = null)
        {
            Console.WriteLine($"testing calculator ({caller})");
            var prod = await calculator.MultiplyAsync(new MultiplyRequest(4, 9));
            Console.WriteLine(prod.Result);
        }


        static async Task TestDuplex(IDuplex duplex, [CallerMemberName] string? caller = null)
        {
            Console.WriteLine($"testing duplex ({caller}) - manual");
            await foreach (var item in duplex.SomeDuplexApiAsync(Rand(10, TimeSpan.FromSeconds(1))))
            {
                Console.WriteLine($"[rec] {item.Result}");
            }

            Console.WriteLine($"testing duplex ({caller}) - auto duplex");
            var channel = System.Threading.Channels.Channel.CreateBounded<MultiplyRequest>(5);
            var ctx = new CallContext(state: channel.Writer);
            var result = duplex.SomeDuplexApiAsync(channel.AsAsyncEnumerable(ctx.CancellationToken));
            await ctx.FullDuplexAsync<MultiplyResult>(s_pumpQueue, result, s_handleResult);
        }

        static readonly Func<CallContext, ValueTask> s_pumpQueue = async ctx =>
        {
            var writer = ctx.As<System.Threading.Channels.ChannelWriter<MultiplyRequest>>();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var item = new MultiplyRequest { X = 40 + i, Y = 40 + i };
                    await writer.WriteAsync(item, ctx.CancellationToken);
                    Console.WriteLine($"[d:sent] {item.X}, {item.Y}");
                    await Task.Delay(TimeSpan.FromSeconds(0.5));
                }
                writer.Complete();
                Console.WriteLine("[d:client all done sending!]");
            }
            catch (Exception ex) { writer.TryComplete(ex); }
        };

        static readonly Func<MultiplyResult, CallContext, ValueTask> s_handleResult = (result, ctx) =>
        {
            Console.WriteLine($"[d:rec] {result.Result}");
            return default;
        };

        static async IAsyncEnumerable<MultiplyRequest> Rand(int count, TimeSpan delay, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Delay(delay, cancellationToken);
                var next = new MultiplyRequest { X = i, Y = i };
                yield return next;
                Console.WriteLine($"[sent] {next.X}, {next.Y}");
            }
            Console.WriteLine("[client all done sending!]");
        }

        static async Task TestChannel()
        {
            var channel = new Channel("localhost", 10042, ChannelCredentials.Insecure);
            try
            {
                // await CallBidiStreamingServiceViaEnumerator(channel);
                await CallBidiStreamingServiceViaChannel(channel);

                //var calculator = channel.CreateGrpcService<ICalculator>();
                //await TestCalculator(calculator);

                //var duplex = channel.CreateGrpcService<IDuplex>();
                //await TestDuplex(duplex);
            }
            finally
            {
                await channel.ShutdownAsync();
            }
        }

#if HTTPCLIENT
        static async Task TestHttpClient()
        {
            GrpcClientFactory.AllowUnencryptedHttp2 = true;
            using var http = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost:10042");

            var calculator = http.CreateGrpcService<ICalculator>();
            await TestCalculator(calculator);

            var duplex = http.CreateGrpcService<IDuplex>();
            await TestDuplex(duplex);
        }
#endif

        private static async Task CallBidiStreamingServiceViaEnumerator(ChannelBase channel)
        {
            var bidiStreamingClient = channel.CreateGrpcService<IBidiStreamingService>();
            var options = new CallContext(flags: CallContextFlags.IgnoreStreamTermination);

            //Read stream - processed on a background task
            try
            {
                await foreach (var response in bidiStreamingClient.TestAsync(SendAsync(), options))
                {
                    Console.WriteLine($"Response received with payload: {response.Payload}");
                }
                Console.WriteLine("success exit of async enumeration");
            }
            catch (InvalidOperationException ioe) when (ioe.Message == "Can't write the message because the call is complete.")
            {
                Console.WriteLine($"IOE exit of async enumeration: {ioe.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"exception exit of async enumeration: {ex.Message}");
                Console.WriteLine(ex.GetType().FullName);
                Console.WriteLine(ex.Source);
            }
            Console.WriteLine("reader is FINISHED; waiting a moment to see what happens with the writer");
            await Task.Delay(10000);
        }

        // note this requires the preview compiler at the moment, for attribs on local method parameters
        static async IAsyncEnumerable<BidiStreamingRequest> SendAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                var n = 0;
                while (true)
                {
                    var request = new BidiStreamingRequest { Payload = $"Payload {n++}" };
                    Console.WriteLine($"Sending request with payload: {request.Payload}");

                    yield return request;

                    //Look busy
                    try
                    {
                        // important: depending on timing , we *might not get here*; this
                        // will only be invoked if the CT isn't *already* cancelled when
                        // MoveNextAsync gets invoked
                        await Task.Delay(1000, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Delay exited: {ex.Message}");
                    }
                }
            }
            finally
            {
                Console.WriteLine("writer is FINISHED");
            }
        }

        private static async Task CallBidiStreamingServiceViaChannel(ChannelBase channel)
        {
            var bidiStreamingClient = channel.CreateGrpcService<IBidiStreamingService>();
            var options = new CallContext(flags: CallContextFlags.IgnoreStreamTermination);

            var pending = System.Threading.Channels.Channel.CreateBounded<BidiStreamingRequest>(5);

            //Read stream - processed on a background task
            var send = Task.Run(() => SendAsync(pending.Writer));
            try
            {
                await foreach (var response in bidiStreamingClient.TestAsync(pending.AsAsyncEnumerable(true), options))
                {
                    Console.WriteLine($"Response received with payload: {response.Payload}");
                }
                Console.WriteLine("success exit of async enumeration");
            }
            catch (InvalidOperationException ioe) when (ioe.Message == "Can't write the message because the call is complete.")
            {
                Console.WriteLine($"IOE exit of async enumeration: {ioe.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"exception exit of async enumeration: {ex.Message}");
                Console.WriteLine(ex.GetType().FullName);
                Console.WriteLine(ex.Source);
            }
            Console.WriteLine("Seeing if our SendAsync exits...");
            try
            {
                await send;
                Console.WriteLine($"SendAsync exited cleanly");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SendAsync faulted: {ex.Message}");
            }

            static async Task SendAsync(System.Threading.Channels.ChannelWriter<BidiStreamingRequest> writer)
            {
                try
                {
                    var n = 0;
                    while (true)
                    {
                        var request = new BidiStreamingRequest { Payload = $"Payload {n++}" };
                        Console.WriteLine($"Sending request with payload: {request.Payload}");

                        await writer.WriteAsync(request);

                        //Look busy
                        await Task.Delay(1000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SendAsync faulted: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("writer is FINISHED");
                }
            }
        }
    }
}