using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
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
        private ClaimsPrincipal ScopePrincipal;
        public AzureGroupsClaimsTransform(IGraphUtilityService graphService, ITokenAcquisition tokenAcquisition,
            IDistributedCache cache, IOptions<GraphUtilityServiceOptions> options)
        {
            GraphService     = graphService;
            TokenAcquisition = tokenAcquisition;
            Options          = options.Value;  
            Cache            = cache;
        }
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            //transform can run more than once.
            //the service is scoped, so setting and testing the Principal property allows short-circuiting
            if (ScopePrincipal != null) return ScopePrincipal;

            ScopePrincipal = principal;
            var claimsIdentity = principal.Identities.First();

            claimsIdentity.AddClaim(new Claim("transform", this.GetType().FullName));

            var groupNames = Enumerable.Empty<String>();

            //TODO: refactor cache operations into extension methods.
            //TODO: add extension method for clearing principal groupsfrom cache

            //cached?
            string json = await Cache.GetStringAsync($"oid.groups:{principal.GetObjectId():N}");
            if (!string.IsNullOrEmpty(json))
            {
                groupNames = JsonSerializer.Deserialize<IEnumerable<string>>(json);
            }
            else
            {
                //call graph api
                //TODO: graph API endpoint from configuration
                string accessToken = await TokenAcquisition.GetAccessTokenForAppAsync(Options.GraphEndpoint);
                var groups = await GraphService.GroupsAsync(principal, accessToken);
                //if ClaimsPrincipal is not from an azure user or something went horribly awry, don't crash
                if (groups == null) return principal; 

                groupNames = (await GraphService.GroupsAsync(principal, accessToken)).Select(x => x.DisplayName);
                json = JsonSerializer.Serialize(groupNames);

                await Cache.SetStringAsync($"oid.groups:{principal.GetObjectId():N}", json, Options.DistributedCacheEntryOptions);
            }

            foreach (var group in groupNames)
            {
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, group, ClaimValueTypes.String, "AzureAD"));
            }

            return principal;
        }
    }
}
