using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using N8T.Infrastructure;
using N8T.Infrastructure.Auth;
using N8T.Infrastructure.Dapr;
using N8T.Infrastructure.EfCore;
using N8T.Infrastructure.Logging;
using N8T.Infrastructure.OTel;
using N8T.Infrastructure.Tye;
using N8T.Infrastructure.Validator;
using SaleService.Domain.Gateway;
using SaleService.Domain.Model;
using SaleService.Infrastructure.Data;
using SaleService.Infrastructure.Gateway;
using SaleService.Infrastructure.Services;

namespace SaleService.Api
{
    public class Startup
    {
        public Startup(IConfiguration config, IWebHostEnvironment env)
        {
            Config = config;
            Env = env;
        }

        private IConfiguration Config { get; }
        private IWebHostEnvironment Env { get; }
        private bool IsRunOnTye => Config.IsRunOnTye();

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHttpContextAccessor()
                .AddCustomMediatR<Anchor>()
                .AddCustomValidators<Anchor>()
                .AddCustomDbContext<MainDbContext, Anchor>(Config.GetConnectionString("postgres"))
                .AddCustomDaprClient()
                .AddControllers()
                .AddDapr();

            services.AddHealthChecks()
                .AddNpgSql(Config.GetConnectionString("postgres"));

            services.AddCustomAuth<Anchor>(Config, options =>
            {
                options.Authority = IsRunOnTye
                    ? Config.GetServiceUri("identityapp")?.AbsoluteUri
                    : options.Authority;

                options.Audience = IsRunOnTye
                    ? $"{Config.GetServiceUri("identityapp")?.AbsoluteUri.TrimEnd('/')}/resources"
                    : options.Audience;
            });

            services.AddScoped<ISecurityContextAccessor, SecurityContextAccessor>();
            services.AddScoped<IUserGateway, UserGateway>();
            services.AddScoped<IInventoryGateway, InventoryGateway>();
            services.AddScoped<IProductCatalogGateway, ProductCatalogGateway>();
            services.AddScoped<IOrderValidationService, OrderValidationService>();

            services.AddCustomOtelWithZipkin(Config,
                o =>
                {
                    o.Endpoint = IsRunOnTye
                        ? new Uri($"http://{Config.GetServiceUri("zipkin")?.DnsSafeHost}:9411/api/v2/spans")
                        : o.Endpoint;
                });
        }

        public void Configure(IApplicationBuilder app)
        {
            if (Env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseCloudEvents();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/healthz", new HealthCheckOptions {Predicate = _ => true});
                endpoints.MapHealthChecks("/liveness",
                    new HealthCheckOptions {Predicate = r => r.Name.Contains("self")});

                endpoints.MapControllers();
                endpoints.MapSubscribeHandler();
            });

            app.ApplicationServices.CreateLoggerConfiguration(IsRunOnTye);
        }
    }
}
