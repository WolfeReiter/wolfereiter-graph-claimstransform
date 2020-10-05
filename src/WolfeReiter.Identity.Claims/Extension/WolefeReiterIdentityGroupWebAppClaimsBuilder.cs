
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WolfeReiter.Identity.Claims;

namespace Microsoft.Identity.Web
{
    public static class WolefeReiterIdentityGroupWebAppClaimsBuilder
    {
        public static void AddWolfeReiterAzureGroupsClaimsTransform(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<GraphUtilityServiceOptions>(configuration.GetSection("WolfeReiterIdentityClaims"));
            services.AddSingleton<IGraphUtilityService, GraphUtilityService>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }

        public static void AddWolfeReiterAzureGroupsClaimsTransform<TGraphUtilityService>(this IServiceCollection services,
            IConfiguration configuration)
            where TGraphUtilityService : class, IGraphUtilityService
        {
            services.Configure<GraphUtilityServiceOptions>(configuration.GetSection("WolfeReiterIdentityClaims"));
            services.AddSingleton<IGraphUtilityService, TGraphUtilityService>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }
    }
}