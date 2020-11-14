﻿using ProtoBuf.Grpc.Configuration;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace protobuf_net.Grpc.Test.Integration
{
    /// <summary>
    /// Uses BinaryFormatter for serialization
    /// </summary>
    [Obsolete("Use of BinaryFormatter is *extremely* discouraged for security, cross-platform compatibility, compatibility-between-versions, and performance reasons")]
    public sealed class BinaryFormatterMarshallerFactory : MarshallerFactory
    {
        public static bool I_Have_Read_The_Notes_On_Not_Using_BinaryFormatter { get; set; } // see a few lines below
        public static bool I_Promise_Not_To_Do_This { get; set; }

        /* THE NOTES ON NOT USING BINARYFORMATTER (note: these notes also apply to NetDataContractSerializer and a few others)

            It Will Hurt You.
            That much is not in question. The only questions are "when", "how", and "how badly".

            This example is included for two reasons:
            
            1) to show you *how* you can write a custom marshaller
            2) to allow me time and space to write this very important treatise!

            -------------------------------------

            Security: BinaryFormatter is a known RCE attack vector; the point of gRPC is to allow communication between
            two end-points, often client-server. In such a scenario you must *assume* that the other end could be hostile.
            For example, someone could reverse-engineer your communications protocol and make their own requests from a
            modified application, or just a 7-line console application. With a suitably crafted payload, such a client
            could compromise your server; this is usually considered "a bad thing". Likewise (but perhaps rarer), a
            suitably motivated attacker could hijack your DNS (etc) and replace the server, compromising the client. If
            you're thinking "I'll provide a custom BinaryFormatter 'binder' to limit this risk": don't; that's just an
            arms race. You only need to lose that race once.

            "But this is service-to-service inside my network and I control every piece"
            It is a good job that attacks never originate internally, then‽ this could be a disgruntled/bribed employee,
            or it could be an escalation attack from one compromised system to other systems; for example, your edge
            web-server in the DMZ with a low access account and restricted network might get compromised (it happens!);
            the attacker could now use RPC-based RCE (via BinaryFormatter etc) to jump to your service tier that is
            in your restricted network and which has access to more systems (databases, file systems, etc). Don't make
            it easy for attackers. BinaryFormatter RCE is almost at the same level as probing a web-site for what happens
            when you try searching for "Fred'; drop table Orders; --"

            "But I just use it for storing state and reloading"
            You might want to ask House House how well that worked out for them:
            https://www.scmagazine.com/home/security-news/vulnerabilities/untitled-goose-game-rce-flaw-revealed/

            Additionally, it is ridiculously easy to accidentally include more in your payload than you intended - a
            classic being not remembering to add [field:NonSerialized] on an event, and accidentally serializing your
            entire UI/application model (basically, your entire application memory) and sending that with your payload.
            I don't care if the other end silently ignores the extra data, or whether it throws a fault while trying
            to deserialize it: *you still sent it*; that's a data leak and is a security hole.

            -------------------------------------

            Cross-platform compatibility: BinaryFormatter will only be usable between .NET applications; the premise of
            gRPC is that you can use it *between* platforms. Using a framework-specific marshaller flies against what
            the tool is trying to do for you. Note that because a lot of libraries moved around between .NET Framework
            and .NET Core (or .NET 5), this also means that it is very unreliable to use BinaryFormatter between
            .NET Framework and .NET Core - or sometimes just between sub-versions of .NET Framework, or sub-versions of
            .NET Core.

            -------------------------------------

            Compatibility between versions: BinaryFormatter is simply brittle. What you might consider minor refactorings
            to your code can completely break the serializer. For example, changing something from an automatically
            implemented property to a property that you write yourself; from the perspective of most serializers, this is
            an implementation detail that it doesn't even see - but because BinaryFormatter works against *fields*, this
            is a breaking change. Likewise, moving types around, renaming them, changing the assembly identity - again, all
            things that most serializers *won't care about*: they break you. There *are* some ways of fixing this, but
            those options are anathema to the "hey, I'll just whack [Serializable] on it" approach that people are
            usually talking about when using BinaryFormatter.

            Performance: it just isn't great. At the serializer layer, it could be faster; but it is also relatively
            large payload-wise compared to other options. There is also the "we sent more data than we expected" issue
            (from the "Security" section), which can bloat payloads significantly.

            So: there are **lots** of reasons not to use BinaryFormatter. Please don't.
        */

        private BinaryFormatterMarshallerFactory() { }
        /// <summary>
        /// Uses BinaryFormatter for serialization
        /// </summary>
        public static MarshallerFactory Default { get; } = new BinaryFormatterMarshallerFactory();

        /// <inheritdoc/>
        protected override bool CanSerialize(Type type)
            => I_Have_Read_The_Notes_On_Not_Using_BinaryFormatter & I_Promise_Not_To_Do_This & type.IsSerializable;

        /// <inheritdoc/>
        protected override T Deserialize<T>(byte[] payload)
        {
            // note: the byte[] API is "simple mode"; there is a much more efficient
            // "pipelines"-based API available via CreateMarshaller<T>()
            using var ms = new MemoryStream(payload);
            return (T)new BinaryFormatter().Deserialize(ms);
        }

        /// <inheritdoc/>
        protected override byte[] Serialize<T>(T value)
        {
            // note: the byte[] API is "simple mode"; there is a much more efficient
            // "pipelines"-based API available via CreateMarshaller<T>()
            using var ms = new MemoryStream();
            new BinaryFormatter().Serialize(ms, value!);
            return ms.ToArray();
        }
    }
}
