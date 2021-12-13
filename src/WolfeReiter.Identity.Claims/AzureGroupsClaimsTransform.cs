using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;


namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// AzureGroupsClaimsTransform is meant to be added as a scoped service in ConfigureServices() of startup.cs.
    /// It will find the Group memberships for the current scope ClaimsPrincipal authenticated by AzureAD and 
    /// add the Group names that user is a member of as Role claims.
    /// 
    /// services.AddScoped&gt;IClaimsTransformation, AzureGroupsClaimsTransform&lt;();
    /// </summary>
    public class AzureGroupsClaimsTransform : IClaimsTransformation
    {
        private readonly GraphUtilityServiceOptions Options;
        private readonly IGraphUtilityService GraphService;
        private readonly ITokenAcquisition TokenAcquisition;
        private readonly IDistributedCache Cache;
        //cache for ClaimsPrincipal from the current scope.
        private ClaimsPrincipal? ScopePrincipal;
        private readonly ILogger Logger;
        public AzureGroupsClaimsTransform(IGraphUtilityService graphService, ITokenAcquisition tokenAcquisition,
            IDistributedCache cache, IOptions<GraphUtilityServiceOptions> options, ILoggerFactory logger)
        {
            GraphService     = graphService;
            TokenAcquisition = tokenAcquisition;
            Options          = options.Value;  
            Cache            = cache;
            Logger           = logger.CreateLogger<AzureGroupsClaimsTransform>();
        }
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            //transform can run more than once.
            //the service is scoped, so setting and testing the Principal property allows short-circuiting
            if (ScopePrincipal != null) return ScopePrincipal;
            ScopePrincipal = principal;

            //if there is no oid, then principal didn't come from AzureAD / OpenIdConnect
            if (principal.GetObjectId() == null) return principal;

            var claimsIdentity = principal.Identities.First();
            //mark principal with claim for this transform
            claimsIdentity.AddClaim(new Claim("transform", GetType()?.FullName ?? "AzureGroupsClaimsTransform"));

            var claimsCacheResult = await Cache.GetGroupClaimsAsync(principal);
            var groupNames        = claimsCacheResult.Value;
            if (!claimsCacheResult.Success)
            {
                var accessToken = await TokenAcquisition.GetAccessTokenForAppAsync(Options.GraphEndpoint, authenticationScheme:Options.Scheme);
                try
                {
                    groupNames = (await GraphService.GroupsAsync(principal, accessToken))?.Select(x => x.DisplayName);
                }
                catch (Exception e)
                {
                    var requestId = Activity.Current?.Id ?? "<null>";
                    Logger.LogCritical(e, "AzureGroupsClaimsTransform exception from RequestId: {requestId}.", requestId);

                    claimsIdentity.AddClaim(new Claim("transform-error", e.Message));
                    claimsIdentity.AddClaim(new Claim("transform-error-request-id", requestId));

                    return principal;
                }
                if (groupNames != null)
                {
                    await Cache.SetGroupClaimsAsync(principal, groupNames, Options.DistributedCacheEntryOptions);
                }
            }

            foreach (var group in groupNames!)
            {
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, group, ClaimValueTypes.String, "AzureAD"));
            }

            return principal;
        }
    }
}
