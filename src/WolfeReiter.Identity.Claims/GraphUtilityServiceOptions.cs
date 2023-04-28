using System;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;

namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityServiceOptions
    {
        //public string GraphApiVersion { get; set; } = "https://graph.microsoft.com/v1.0";
        public string GraphEndpoint { get; set; } = "https://graph.microsoft.com/.default";
        public int GroupClaimsCacheAbsoluteExpirationHours { get; set; } = 24;
        public int GroupClaimsCacheSlidingExpirationMinutes { get; set; } = 30;
        public DistributedCacheEntryOptions DistributedCacheEntryOptions =>
            new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(
                    DateTime.UtcNow.AddHours(GroupClaimsCacheAbsoluteExpirationHours)
                )
                .SetSlidingExpiration(
                    TimeSpan.FromMinutes(GroupClaimsCacheSlidingExpirationMinutes)
                );
        public string Scheme = OpenIdConnectDefaults.AuthenticationScheme;

        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
