using CVGrpcCppSharpService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Grpc.Server;
using Shared_CS;
using System;
using System.Diagnostics;

namespace Server_CS
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCodeFirstGrpc(config =>
            {
                config.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Optimal;
            });
            services.TryAddSingleton(BinderConfiguration.Create(binder: new ServiceBinderWithServiceResolutionFromServiceCollection(services)));
            services.AddCodeFirstGrpcReflection();

            services.AddAuthentication(FakeAuthHandler.SchemeName)
                .AddScheme<FakeAuthOptions, FakeAuthHandler>(FakeAuthHandler.SchemeName, options => options.AlwaysAuthenticate = true);
            services.AddAuthorization();
            services.AddSingleton<ICounter, MyCounter>();
            services.AddSingleton<ICVDotNetLogger, DummyService>();
            services.AddSingleton<INativeObjectsMemoryAddressManager, DummyService>();
        }

        class DummyService : ICVDotNetLogger, INativeObjectsMemoryAddressManager { }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment _)
        {
            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<ICounter>();
                endpoints.MapGrpcService<MyCalculator>();
                endpoints.MapGrpcService<MyTimeService>();

                var watch = Stopwatch.StartNew();
                endpoints.MapGrpcService<EvSecurityCheckManagedCppSharpService>();
                watch.Stop();
                Console.WriteLine($"1: {watch.ElapsedMilliseconds}ms");

                watch.Restart();
                endpoints.MapGrpcService<EvSecurityNewManagedCppSharpService>();
                watch.Stop();
                Console.WriteLine($"2: {watch.ElapsedMilliseconds}ms");

                endpoints.MapCodeFirstGrpcReflectionService();
            });
        }
    }
}
