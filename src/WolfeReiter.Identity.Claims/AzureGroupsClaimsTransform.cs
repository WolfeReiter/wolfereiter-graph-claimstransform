using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// AzureGroupsClaimsTransform is meant to be added as a scoped service in ConfigureServices() of startup.cs.
    /// It reads the group Object IDs Entra ID puts in the <c>groups</c> claim of the token and adds a
    /// Role claim for each one found in the <see cref="GroupRoleMap"/>. Entra ID is not queried
    /// during sign in.
    ///
    /// services.AddScoped&gt;IClaimsTransformation, AzureGroupsClaimsTransform&lt;();
    /// </summary>
    public class AzureGroupsClaimsTransform : IClaimsTransformation
    {
        public const string GroupsClaimType = "groups";

        private readonly GroupRoleMap RoleMap;

        //cache for ClaimsPrincipal from the current scope.
        private ClaimsPrincipal? ScopePrincipal;
        private readonly ILogger Logger;

        public AzureGroupsClaimsTransform(GroupRoleMap roleMap, ILoggerFactory logger)
        {
            RoleMap = roleMap;
            Logger = logger.CreateLogger<AzureGroupsClaimsTransform>();
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            //transform can run more than once.
            //the service is scoped, so setting and testing the Principal property allows short-circuiting
            if (ScopePrincipal != null)
                return ScopePrincipal;
            ScopePrincipal = principal;

            //if there is no oid, then principal didn't come from AzureAD / OpenIdConnect
            if (principal.GetObjectId() == null)
                return principal;

            var claimsIdentity = principal.Identities.First();
            //mark principal with claim for this transform
            claimsIdentity.AddClaim(
                new Claim("transform", GetType()?.FullName ?? "AzureGroupsClaimsTransform")
            );

            if (HasGroupsOverage(principal))
            {
                const string message =
                    "The token omits the groups claim because the user belongs to too many groups. "
                    + "Limit the app registration's groups claim to groups assigned to the application.";
                Logger.LogWarning(
                    "{Message} Object ID {ObjectId}.",
                    message,
                    principal.GetObjectId()
                );
                claimsIdentity.AddClaim(new Claim("transform-error", message));
            }

            IReadOnlyDictionary<Guid, string> map;
            try
            {
                map = await RoleMap.GetAsync();
            }
            catch (Exception e)
            {
                // a group name with no match or several matches, or any other failure to build
                // the map, is critical and is not handled here
                Logger.LogCritical(
                    e,
                    "Group role map failed in RequestId {RequestId}. {Message}",
                    Activity.Current?.Id ?? "<null>",
                    e.Message
                );
                throw;
            }

            var roles = principal
                .FindAll(GroupsClaimType)
                .Select(x =>
                    Guid.TryParse(x.Value, out var id) && map.TryGetValue(id, out var role)
                        ? role
                        : null
                )
                .Where(x => x != null)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var role in roles)
            {
                if (!claimsIdentity.HasClaim(ClaimTypes.Role, role!))
                    claimsIdentity.AddClaim(
                        new Claim(ClaimTypes.Role, role!, ClaimValueTypes.String, "AzureAD")
                    );
            }

            return principal;
        }

        /// <summary>
        /// Entra ID leaves out the groups claim and points to Graph instead when the user is in
        /// more groups than fit in the token.
        /// </summary>
        private static bool HasGroupsOverage(ClaimsPrincipal principal) =>
            principal.HasClaim(x => x.Type == "hasgroups")
            || principal.HasClaim(x =>
                x.Type == "_claim_names" && x.Value.Contains("\"groups\"", StringComparison.Ordinal)
            );
    }
}
