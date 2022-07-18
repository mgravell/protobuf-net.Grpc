﻿using Grpc.Core;
using ProtoBuf.Grpc.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ProtoBuf.Grpc.Client
{
    /// <summary>
    /// Provides extension methods to the native Channel API
    /// </summary>
    public static class GrpcClientFactory
    {
#if NET5_0_OR_GREATER
        // it is *intended* that this attribute usage will help the linker not remove things that we need
        // see: https://docs.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming#dynamicallyaccessedmembers
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
        internal static readonly Type ClientBaseType = typeof(ClientBase);

        private const string SwitchAllowUnencryptedHttp2 = "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport";
        /// <summary>
        /// Allows HttpClient to use HTTP/2 without TLS
        /// </summary>
        public static bool AllowUnencryptedHttp2
        {
            get => AppContext.TryGetSwitch(SwitchAllowUnencryptedHttp2, out var enabled) && enabled;
            set => AppContext.SetSwitch(SwitchAllowUnencryptedHttp2, value);
        }

        /// <summary>
        /// Creates a code-first service backed by a ChannelBase instance
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TService CreateGrpcService<TService>(this ChannelBase client, ClientFactory? clientFactory = null)
            where TService : class
                    => CreateGrpcService<TService>(client.CreateCallInvoker(), clientFactory);

        /// <summary>
        /// Creates a code-first service backed by a ChannelBase instance
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TService CreateGrpcService<TService>(this CallInvoker client, ClientFactory? clientFactory = null)
            where TService : class
                    => (clientFactory ?? ClientFactory.Default).CreateClient<TService>(client);

        /// <summary>
        /// Creates a general purpose service backed by a ChannelBase instance
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GrpcClient CreateGrpcService(this ChannelBase client, Type serviceType, ClientFactory? clientFactory = null)
            => CreateGrpcService(client.CreateCallInvoker(), serviceType, clientFactory);

        /// <summary>
        /// Creates a general purpose service backed by a ChannelBase instance
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GrpcClient CreateGrpcService(this CallInvoker client, Type serviceType, ClientFactory? clientFactory = null)
            => (clientFactory ?? ClientFactory.Default).CreateClient(client, serviceType);
    }
}
