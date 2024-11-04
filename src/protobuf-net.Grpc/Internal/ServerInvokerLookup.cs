using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace ProtoBuf.Grpc.Internal
{
    internal static class ServerInvokerLookup
    {
        internal static int GeneralPurposeSignatureCount() => _invokers.Keys.Count(x => x.Context == ContextKind.CallContext || x.Context == ContextKind.NoContext || x.Context == ContextKind.CancellationToken);

        static Expression ToTaskT(Expression expression)
        {
            var type = expression.Type;
            if (type == typeof(void))
            {
                // no result from the call; add in Empty.Instance instead
                var field = Expression.Field(null, ProxyEmitter.s_Empty_InstaneTask);
                return Expression.Block(expression, field);
            }
#pragma warning disable CS0618 // Reshape
            if (type == typeof(ValueTask))
                return Expression.Call(typeof(Reshape), nameof(Reshape.EmptyValueTask), null, expression);
            if (type == typeof(Task))
                return Expression.Call(typeof(Reshape), nameof(Reshape.EmptyTask), null, expression);
#pragma warning restore CS0618

            if (type.IsGenericType)
            {
                if (type.GetGenericTypeDefinition() == typeof(Task<>))
                    return expression;
                if (type.GetGenericTypeDefinition() == typeof(ValueTask<>))
                    return Expression.Call(expression, nameof(ValueTask<int>.AsTask), null);
            }
            return Expression.Call(typeof(Task), nameof(Task.FromResult), [expression.Type], expression);
        }

        internal static readonly ConstructorInfo s_CallContext_FromServerContext = typeof(CallContext).GetConstructor([typeof(object), typeof(ServerCallContext)])!;
        internal static readonly PropertyInfo s_ServerContext_CancellationToken = typeof(ServerCallContext).GetProperty(nameof(ServerCallContext.CancellationToken))!;

#pragma warning disable CS0618, CA1859, IDE0060 // Reshape [Obsolete], explicit return type, unused arg
        static Expression ToCallContext(Expression server, Expression context) => Expression.New(s_CallContext_FromServerContext, server, context);
        static Expression ToCancellationToken(Expression context) => Expression.Property(context, s_ServerContext_CancellationToken);

        static Expression AsAsyncEnumerable(Expression value, Expression context)
            => Expression.Call(typeof(Reshape), nameof(Reshape.AsAsyncEnumerable),
                typeArguments: value.Type.GetGenericArguments(),
                arguments: [value, Expression.Property(context, nameof(ServerCallContext.CancellationToken))]);

        static Expression AsObservable(Expression value, Expression context)
            => Expression.Call(typeof(Reshape), nameof(Reshape.AsObservable),
                typeArguments: value.Type.GetGenericArguments(),
                arguments: [value]);

        static Expression WriteTo(Expression value, Expression writer, Expression context)
            => Expression.Call(typeof(Reshape), nameof(Reshape.WriteTo),
                typeArguments: value.Type.GetGenericArguments(),
                arguments: [value, writer, Expression.Property(context, nameof(ServerCallContext.CancellationToken))]);

        static Expression WriteObservableTo(Expression value, Expression writer, Expression context)
            => Expression.Call(typeof(Reshape), nameof(Reshape.WriteObservableTo),
                typeArguments: value.Type.GetGenericArguments(),
                arguments: [value, writer]);

        static Expression WriteStream(Expression value, Expression writer, Expression context, bool writeTrailer = true)
            => Expression.Call(typeof(Reshape), nameof(Reshape.WriteStream),
                typeArguments: null,
                arguments: [ToTaskT(value), writer, context, ConstantBoolean(writeTrailer)]);

        private static Expression ConstantBoolean(bool value) => value ? True : False;
        private static readonly Expression True = Expression.Constant(true, typeof(bool)), False = Expression.Constant(false, typeof(bool));

        internal static bool TryGetValue(MethodType MethodType, ContextKind Context, ResultKind Arg, ResultKind Result, VoidKind Void, out Func<MethodInfo, Expression[], Expression>? invoker)
            => _invokers.TryGetValue((MethodType, Context, Arg, Result, Void), out invoker);
#pragma warning restore CS0618, CA1859,IDE0060

        private static readonly Dictionary<(MethodType Method, ContextKind Context, ResultKind Arg,ResultKind Result, VoidKind Void), Func<MethodInfo, Expression[], Expression>?> _invokers
            = new Dictionary<(MethodType, ContextKind, ResultKind, ResultKind, VoidKind), Func<MethodInfo, Expression[], Expression>?>
        {
                // GRPC-style server methods are direct match; no mapping required
                // => service.{method}(args)
                { (MethodType.Unary, ContextKind.ServerCallContext, ResultKind.Sync, ResultKind.Task, VoidKind.None), null },
                { (MethodType.ServerStreaming, ContextKind.ServerCallContext, ResultKind.Sync, ResultKind.Task, VoidKind.None), null },
                { (MethodType.ClientStreaming, ContextKind.ServerCallContext, ResultKind.Grpc, ResultKind.Task, VoidKind.None), null },
                { (MethodType.DuplexStreaming, ContextKind.ServerCallContext, ResultKind.Grpc, ResultKind.Task, VoidKind.None), null },

                // Unary: Task<TResponse> Foo(TService service, TRequest request, ServerCallContext serverCallContext);
                // => service.{method}(request, [new CallContext(serverCallContext)])
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Sync, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Sync, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Sync, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },

                
                // Unary: Task<TResponse> Foo(TService service, TRequest request, ServerCallContext serverCallContext);
                // => service.{method}(request, [new CallContext(serverCallContext)]) return Empty.Instance;
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1])) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Sync, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, args[1], ToCancellationToken(args[2]))) },

                // Unary: Task<TResponse> Foo(TService service, TRequest request, ServerCallContext serverCallContext);
                // => service.{method}([new CallContext(serverCallContext)])
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Task, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Task, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Task, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Sync, VoidKind.Request), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },

                
                // Unary: Task<TResponse> Foo(TService service, TRequest request, ServerCallContext serverCallContext);
                // => service.{method}([new CallContext(serverCallContext)]) return Empty.Instance;
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Task, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.NoContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method)) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Task, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CallContext, ResultKind.Sync, ResultKind.Sync, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCallContext(args[0], args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Task, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTask, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },
                {(MethodType.Unary, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Sync, VoidKind.Both), (method, args) => ToTaskT(Expression.Call(args[0], method, ToCancellationToken(args[2]))) },

                // Client Streaming: Task<TResponse> Foo(TService service, IAsyncStreamReader<TRequest> stream, ServerCallContext serverCallContext);
                // => service.{method}(reader.AsAsyncEnumerable(serverCallContext.CancellationToken), [new CallContext(serverCallContext)])
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCallContext(args[0], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCallContext(args[0], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCancellationToken(args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCancellationToken(args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCallContext(args[0], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCallContext(args[0], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.AsyncEnumerable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCancellationToken(args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.AsyncEnumerable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[2]), ToCancellationToken(args[2]))) },

                // and the same for observables
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.Observable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.Observable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.Observable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCallContext(args[0], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.Observable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCallContext(args[0], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.Observable, ResultKind.Task, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCancellationToken(args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.Observable, ResultKind.ValueTask, VoidKind.None), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCancellationToken(args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.Observable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.NoContext, ResultKind.Observable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.Observable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCallContext(args[0], args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CallContext, ResultKind.Observable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCallContext(args[0], args[2]))) },

                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.Observable, ResultKind.Task, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCancellationToken(args[2]))) },
                {(MethodType.ClientStreaming, ContextKind.CancellationToken, ResultKind.Observable, ResultKind.ValueTask, VoidKind.Response), (method, args) => ToTaskT(Expression.Call(args[0], method, AsObservable(args[1], args[2]), ToCancellationToken(args[2]))) },

                // Server Streaming: Task Foo(TService service, TRequest request, IServerStreamWriter<TResponse> stream, ServerCallContext serverCallContext);
                // => service.{method}(request, [new CallContext(serverCallContext)]).WriteTo(stream, serverCallContext.CancellationToken)
                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, args[1]), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, args[1], ToCancellationToken(args[3])), args[2], args[3])},

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.Request), (method, args) => WriteTo(Expression.Call(args[0], method), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.Request), (method, args) => WriteTo(Expression.Call(args[0], method, ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.AsyncEnumerable, VoidKind.Request), (method, args) => WriteTo(Expression.Call(args[0], method, ToCancellationToken(args[3])), args[2], args[3])},

                // and the same for observables
                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, args[1]), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, args[1], ToCancellationToken(args[3])), args[2], args[3])},

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.Observable, VoidKind.Request), (method, args) => WriteObservableTo(Expression.Call(args[0], method), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.Observable, VoidKind.Request), (method, args) => WriteObservableTo(Expression.Call(args[0], method, ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.Observable, VoidKind.Request), (method, args) => WriteObservableTo(Expression.Call(args[0], method, ToCancellationToken(args[3])), args[2], args[3])},

                // Duplex: Task Foo(TService service, IAsyncStreamReader<TRequest> input, IServerStreamWriter<TResponse> output, ServerCallContext serverCallContext);
                // => service.{method}(input.AsAsyncEnumerable(serverCallContext.CancellationToken), [new CallContext(serverCallContext)]).WriteTo(output, serverCallContext.CancellationToken)
                {(MethodType.DuplexStreaming, ContextKind.NoContext, ResultKind.AsyncEnumerable, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[3])), args[2], args[3]) },
                {(MethodType.DuplexStreaming, ContextKind.CallContext, ResultKind.AsyncEnumerable, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[3]), ToCallContext(args[0], args[3])), args[2], args[3]) },
                {(MethodType.DuplexStreaming, ContextKind.CancellationToken, ResultKind.AsyncEnumerable, ResultKind.AsyncEnumerable, VoidKind.None), (method, args) => WriteTo(Expression.Call(args[0], method, AsAsyncEnumerable(args[1], args[3]), ToCancellationToken(args[3])), args[2], args[3]) },

                // and for observables
                {(MethodType.DuplexStreaming, ContextKind.NoContext, ResultKind.Observable, ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, AsObservable(args[1], args[3])), args[2], args[3]) },
                {(MethodType.DuplexStreaming, ContextKind.CallContext, ResultKind.Observable,ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, AsObservable(args[1], args[3]), ToCallContext(args[0], args[3])), args[2], args[3]) },
                {(MethodType.DuplexStreaming, ContextKind.CancellationToken, ResultKind.Observable, ResultKind.Observable, VoidKind.None), (method, args) => WriteObservableTo(Expression.Call(args[0], method, AsObservable(args[1], args[3]), ToCancellationToken(args[3])), args[2], args[3]) },

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.TaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.TaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method, ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.TaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method, ToCancellationToken(args[3])), args[2], args[3])},

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.TaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1]), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.TaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.TaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1], ToCancellationToken(args[3])), args[2], args[3])},

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method, ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.Request), (method, args) => WriteStream(Expression.Call(args[0], method, ToCancellationToken(args[3])), args[2], args[3])},

                {(MethodType.ServerStreaming, ContextKind.NoContext, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1]), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CallContext, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1], ToCallContext(args[0], args[3])), args[2], args[3])},
                {(MethodType.ServerStreaming, ContextKind.CancellationToken, ResultKind.Sync, ResultKind.ValueTaskStream, VoidKind.None), (method, args) => WriteStream(Expression.Call(args[0], method, args[1], ToCancellationToken(args[3])), args[2], args[3])},
        };
    }
}
