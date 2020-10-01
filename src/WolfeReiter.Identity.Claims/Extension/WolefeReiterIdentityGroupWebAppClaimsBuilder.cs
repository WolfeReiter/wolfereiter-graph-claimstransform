
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using WolfeReiter.Identity.Claims;

namespace Microsoft.Identity.Web
{
    public static class WolefeReiterIdentityGroupWebAppClaimsBuilder
    {
        public static void AddWolfeReiterAzureGroupsClaimsTransform(this IServiceCollection services)
        {
            services.AddSingleton<IGraphUtilityService, GraphUtilityService>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }
    }
}