using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WolfeReiter.Identity.Claims;

namespace Microsoft.Identity.Web
{
    public static class WolfeReiterIdentityGroupWebAppClaimsBuilder
    {
        public static void AddWolfeReiterAzureGroupsClaimsTransform(
            this IServiceCollection services,
            IConfiguration configuration
        )
        {
            services
                .Configure<GraphUtilityServiceOptions>(
                    configuration.GetSection("WolfeReiterIdentityClaims")
                )
                .AddOptions<GraphUtilityServiceOptions>()
                .Bind(configuration.GetSection("AzureAD"));
            services.AddSingleton<IGraphUtilityService, GraphUtilityService>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }

        public static void AddWolfeReiterAzureGroupsClaimsTransform<TGraphUtilityService>(
            this IServiceCollection services,
            IConfiguration configuration
        )
            where TGraphUtilityService : class, IGraphUtilityService
        {
            services
                .Configure<GraphUtilityServiceOptions>(
                    configuration.GetSection("WolfeReiterIdentityClaims")
                )
                .AddOptions<GraphUtilityServiceOptions>()
                .Bind(configuration.GetSection("AzureAD"));
            services.AddSingleton<IGraphUtilityService, TGraphUtilityService>();
            //add TGraphUtilityService as TGraphUtilityService so that a ctor service injection reference
            //does not require casting IGraphUtilityService to TGraphUtilityService
            services.AddSingleton<TGraphUtilityService>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }
    }
}
