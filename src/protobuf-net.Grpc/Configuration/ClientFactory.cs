﻿using Grpc.Core;
using ProtoBuf.Grpc.Client;
using ProtoBuf.Grpc.Internal;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ProtoBuf.Grpc.Configuration
{
    /// <summary>
    /// Provides services for creating service clients (proxies)
    /// </summary>
    public abstract class ClientFactory
    {
        /// <summary>
        /// Create a new client factory; note that non-default factories should be considered expensive, and stored/re-used suitably
        /// </summary>
        public static ClientFactory Create(BinderConfiguration? binderConfiguration = null)
            => (binderConfiguration == null) ? new DefaultClientFactory() : new ConfiguredClientFactory(binderConfiguration);

        /// <summary>
        /// Get the binder configuration associated with this instance
        /// </summary>
        protected abstract BinderConfiguration BinderConfiguration { get; }

        /// <summary>
        /// Get the binder configuration associated with this instance
        /// </summary>
        public static implicit operator BinderConfiguration(ClientFactory? value) => value?.BinderConfiguration ?? new BinderConfiguration();

        /// <summary>
        /// Create a service-client backed by a CallInvoker
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public abstract TService CreateClient<TService>(CallInvoker channel) where TService : class;

        /// <summary>
        /// Create a service-client backed by a CallInvoker
        /// </summary>
        public virtual GrpcClient CreateClient(CallInvoker channel, Type contractType)
            => new GrpcClient(channel, contractType, BinderConfiguration);


        private sealed class ConfiguredClientFactory : ClientFactory
        {
            protected override BinderConfiguration BinderConfiguration { get; }

            public ConfiguredClientFactory(BinderConfiguration? binderConfiguration)
            {
                BinderConfiguration = binderConfiguration ?? new BinderConfiguration();
            }

            private readonly ConcurrentDictionary<Type, object> _proxyCache = new ConcurrentDictionary<Type, object>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            private TService SlowCreateClient<TService>(CallInvoker channel)
                where TService : class
            {
                var factory = ProxyEmitter.CreateFactory<TService>(BinderConfiguration);
                var key = typeof(TService);

                if (!_proxyCache.TryAdd(key, factory)) factory = (Func<CallInvoker, TService>)_proxyCache[key];
                return factory(channel);
            }
            public override TService CreateClient<TService>(CallInvoker channel)
                where TService : class
            {
                if (_proxyCache.TryGetValue(typeof(TService), out var obj))
                    return ((Func<CallInvoker, TService>)obj)(channel);
                return SlowCreateClient<TService>(channel);
            }
        }

        internal static class DefaultProxyCache<TService> where TService : class
        {
            internal static readonly Func<CallInvoker, TService> Create = ProxyEmitter.CreateFactory<TService>(new BinderConfiguration());
        }

        private sealed class DefaultClientFactory : ClientFactory
        {
            protected override BinderConfiguration BinderConfiguration => new BinderConfiguration();

            internal DefaultClientFactory() { }

            public override TService CreateClient<TService>(CallInvoker channel) => DefaultProxyCache<TService>.Create(channel);
        }
    }
}
