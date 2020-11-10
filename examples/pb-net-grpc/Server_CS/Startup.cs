using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.Server;
using Shared_CS;

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
            })
            .WithBinderConfiguration(config => config.ServiceBinder = new ServiceBinderWithServiceResolutionFromServiceCollection(services));
            services.AddCodeFirstGrpcReflection();

            services.AddAuthentication(options =>
            {
                options.AddScheme<FakeAuthHandler>(FakeAuthHandler.SchemeName, FakeAuthHandler.SchemeName);
                options.DefaultScheme = FakeAuthHandler.SchemeName;
            });
            services.AddAuthorization();
            services.AddSingleton<ICounter, MyCounter>();
        }

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
                endpoints.MapCodeFirstGrpcReflectionService();
            });
        }
    }
}
