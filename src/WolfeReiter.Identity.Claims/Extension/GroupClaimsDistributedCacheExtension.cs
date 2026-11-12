using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using WolfeReiter.Threading.Tasks;

namespace Microsoft.Extensions.Caching.Distributed
{
    [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
    public static class GroupClaimsDistributedExtension
    {
        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public static Task SetGroupClaimsAsync(
            this IDistributedCache cache,
            ClaimsPrincipal principal,
            IEnumerable<string> groupNames,
            DistributedCacheEntryOptions options
        ) => throw new NotSupportedException("Role claims come from the token groups claim and are no longer cached per user.");

        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public static Task<AsyncResult<IEnumerable<string>>> GetGroupClaimsAsync(
            this IDistributedCache cache,
            ClaimsPrincipal principal
        ) => throw new NotSupportedException("Role claims come from the token groups claim and are no longer cached per user.");

        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public static Task RemoveGroupClaimsAsync(
            this IDistributedCache cache,
            ClaimsPrincipal principal
        ) => throw new NotSupportedException("Role claims come from the token groups claim and are no longer cached per user.");
    }
}
