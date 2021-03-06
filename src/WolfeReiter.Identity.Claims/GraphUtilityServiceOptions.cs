
using System;
using Microsoft.Extensions.Caching.Distributed;
namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityServiceOptions
    {
        public string GraphApiVersion { get; set; } = "https://graph.microsoft.com/v1.0";
        public string GraphEndpoint { get; set; }  = "https://graph.microsoft.com/.default";
        public int GroupClaimsCacheAbsoluteExpirationHours { get; set; } = 24;
        public int GroupClaimsCacheSlidingExpirationMinutes { get; set; } = 30;
        public DistributedCacheEntryOptions DistributedCacheEntryOptions => new DistributedCacheEntryOptions()
                        .SetAbsoluteExpiration(DateTime.Now.AddHours(GroupClaimsCacheAbsoluteExpirationHours))
                        .SetSlidingExpiration(TimeSpan.FromMinutes(GroupClaimsCacheSlidingExpirationMinutes));
    }
}