﻿using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;

namespace ProtoBuf.Grpc.Lite.Internal;

internal static class Utilities
{
    public static readonly byte[] EmptyBuffer = Array.Empty<byte>(); // static readonly field to make the JIT's life easy

    [Conditional("DEBUG")]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogDebug<TState>(this ILogger? logger, TState state, Func<TState, Exception?, string> formatter, Exception? exception = null)
        => logger?.Log<TState>(LogLevel.Debug, default, state, exception, formatter);

    public static void SafeDispose(this IDisposable? disposable)
    {
        if (disposable is not null)
        {
            try { disposable.Dispose(); }
            catch { }
        }
    }
    public static ValueTask SafeDisposeAsync(this IAsyncDisposable? disposable)
    {
        if (disposable is not null)
        {
            try
            {
                var pending = disposable.DisposeAsync();
                if (!pending.IsCompleted) return CatchAsync(pending);
                // we always need to observe it, for both success and failure
                pending.GetAwaiter().GetResult();
            }
            catch { } // swallow
        }
        return default;

        static async ValueTask CatchAsync(ValueTask pending)
        {
            try { await pending; }
            catch { } // swallow
        }
    }

    public static ValueTask SafeDisposeAsync(IAsyncDisposable? first, IAsyncDisposable? second)
    {
        // handle null/same
        if (first is null || ReferenceEquals(first, second)) return second.SafeDisposeAsync();
        if (second is null) return first.SafeDisposeAsync();

        // so: different
        var firstPending = first.SafeDisposeAsync();
        var secondPending = second.SafeDisposeAsync();
        if (firstPending.IsCompletedSuccessfully)
        {
            firstPending.GetAwaiter().GetResult(); // ensuure observed
            return secondPending;
        }
        if (secondPending.IsCompletedSuccessfully)
        {
            secondPending.GetAwaiter().GetResult();
            return firstPending;
        }
        // so: neither completed synchronously!
        return Both(firstPending, secondPending);
        static async ValueTask Both(ValueTask first, ValueTask second)
        {
            await first;
            await second;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogInformation<TState>(this ILogger? logger, TState state, Func<TState, Exception?, string> formatter, Exception? exception = null)
        => logger?.Log<TState>(LogLevel.Information, default, state, exception, formatter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogError<TState>(this ILogger? logger, TState state, Func<TState, Exception?, string> formatter, Exception? exception = null)
        => logger?.Log<TState>(LogLevel.Error, default, state, exception, formatter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LogCritical<TState>(this ILogger? logger, TState state, Func<TState, Exception?, string> formatter, Exception? exception = null)
        => logger?.Log<TState>(LogLevel.Critical, default, state, exception, formatter);

    public static readonly Task<bool> AsyncTrue = Task.FromResult(true), AsyncFalse = Task.FromResult(false);


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort IncrementToUInt32(ref int value)
        => unchecked((ushort)Interlocked.Increment(ref value));

    public static IGatedTerminator Gate(this ITerminator terminator, int inputBuffer, int outputBuffer)
    {
        if (terminator is IGatedTerminator gated) return gated; // already gated

        return inputBuffer == 0 ? new SynchronizedGate(terminator, outputBuffer)
            : new BufferedGate(terminator, inputBuffer, outputBuffer);
    }
}
