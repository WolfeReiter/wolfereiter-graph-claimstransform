using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Identity.Web;
using WolfeReiter.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed
{
    public static class GroupClaimsDistributedExtension
    {
        public static async Task SetGroupClaimsAsync(this IDistributedCache cache, ClaimsPrincipal principal, IEnumerable<string> groupNames, DistributedCacheEntryOptions options)
        {
            if (principal == null || principal.GetObjectId() == null) return;

            var json = JsonSerializer.Serialize(groupNames);
            await cache.SetStringAsync(CacheKey(principal), json, options);
        }

        public static async Task<AsyncResult<IEnumerable<string>>> GetGroupClaimsAsync(this IDistributedCache cache, ClaimsPrincipal principal)
        {
            var result = new AsyncResult<IEnumerable<string>>();
            if (principal != null && principal.GetObjectId() != null)
            {
                string json = await cache.GetStringAsync(CacheKey(principal)) ?? string.Empty;
                if (!string.IsNullOrEmpty(json)) 
                {
                    result.Success    = true;
                    result.Value = JsonSerializer.Deserialize<IEnumerable<string>>(json);
                }
            }
            return result;
        }

        public static async Task RemoveGroupClaimsAsync(this IDistributedCache cache, ClaimsPrincipal principal)
        {
            if (principal == null || principal.GetObjectId() == null) return;

            await cache.RemoveAsync(CacheKey(principal));
        }
        static string CacheKey(ClaimsPrincipal principal) =>  $"principal.oid.groups:{principal.GetObjectId():N}";
    }
}