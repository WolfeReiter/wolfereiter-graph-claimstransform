using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// AzureGroupsClaimsTransform is meant to be added as a scoped service in ConfigureServices() of startup.cs.
    /// It reads the group Object IDs Entra ID puts in the <c>groups</c> claim of the token and adds a
    /// Role claim for each one found in the <see cref="GroupRoleMap"/>. Only the identity holding the
    /// <c>oid</c> claim is read, and only groups claims with the same issuer, which must be the
    /// configured tenant when TenantId is a GUID. Entra ID is not queried during sign in.
    ///
    /// services.AddScoped&gt;IClaimsTransformation, AzureGroupsClaimsTransform&lt;();
    /// </summary>
    public class AzureGroupsClaimsTransform : IClaimsTransformation
    {
        public const string GroupsClaimType = "groups";

        /// <summary>Issuer of the marker claim and the role claims this transform adds.</summary>
        public const string TransformIssuer = "WolfeReiter.Identity.Claims";

        private readonly GroupRoleMap RoleMap;
        private readonly GraphUtilityServiceOptions Options;
        private readonly ILogger Logger;

        //principals already handled in this scope, compared by reference
        private readonly HashSet<ClaimsPrincipal> Handled = new(ReferenceEqualityComparer.Instance);

        public AzureGroupsClaimsTransform(
            GroupRoleMap roleMap,
            IOptions<GraphUtilityServiceOptions> options,
            ILoggerFactory logger
        )
        {
            RoleMap = roleMap;
            Options = options.Value;
            Logger = logger.CreateLogger<AzureGroupsClaimsTransform>();
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            //transform can run more than once for the same principal in a request
            if (!Handled.Add(principal))
                return principal;

            //only the identity Entra ID issued, found by its oid claim, is trusted for groups
            var (identity, oid) = EntraIdentity(principal);
            if (identity == null || oid == null)
                return principal;
            if (identity.HasClaim(x => x.Type == "transform" && x.Issuer == TransformIssuer))
                return principal;

            if (!IsTenantIssuer(oid.Issuer))
            {
                Logger.LogWarning(
                    "Object ID claim issuer {Issuer} is not tenant {TenantId}, so no roles are added.",
                    oid.Issuer,
                    Options.TenantId
                );
                return principal;
            }

            identity.AddClaim(
                new Claim("transform", GetType().FullName!, ClaimValueTypes.String, TransformIssuer)
            );

            if (HasGroupsOverage(identity))
            {
                const string message =
                    "The token omits the groups claim because the user belongs to too many groups. "
                    + "Limit the app registration's groups claim to groups assigned to the application.";
                Logger.LogWarning("{Message} Object ID {ObjectId}.", message, oid.Value);
                identity.AddClaim(
                    new Claim("transform-error", message, ClaimValueTypes.String, TransformIssuer)
                );
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

            var groups = identity.FindAll(GroupsClaimType).ToList();
            var foreign = groups.Count(x => !string.Equals(x.Issuer, oid.Issuer, StringComparison.Ordinal));
            if (foreign > 0)
                Logger.LogWarning(
                    "{Count} groups claims for Object ID {ObjectId} have an issuer other than {Issuer} and are ignored.",
                    foreign,
                    oid.Value,
                    oid.Issuer
                );

            var roles = groups
                .Where(x => string.Equals(x.Issuer, oid.Issuer, StringComparison.Ordinal))
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
                if (!identity.HasClaim(identity.RoleClaimType, role!) && !identity.HasClaim(ClaimTypes.Role, role!))
                    identity.AddClaim(
                        new Claim(ClaimTypes.Role, role!, ClaimValueTypes.String, TransformIssuer)
                    );
            }

            return principal;
        }

        private static readonly string[] ObjectIdClaimTypes =
        {
            ClaimConstants.Oid,
            ClaimConstants.ObjectId,
        };

        private static (ClaimsIdentity? Identity, Claim? Oid) EntraIdentity(ClaimsPrincipal principal)
        {
            foreach (var identity in principal.Identities)
            {
                var oid = identity.Claims.FirstOrDefault(x => ObjectIdClaimTypes.Contains(x.Type));
                if (oid != null)
                    return (identity, oid);
            }
            return (null, null);
        }

        /// <summary>
        /// When TenantId is a GUID the oid claim must come from that tenant, through either the
        /// v2.0 or the v1.0 issuer.
        /// </summary>
        private bool IsTenantIssuer(string issuer)
        {
            if (!Guid.TryParse(Options.TenantId, out var tenant))
                return true;
            return string.Equals(issuer, $"https://login.microsoftonline.com/{tenant:D}/v2.0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(issuer, $"https://sts.windows.net/{tenant:D}/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Entra ID leaves out the groups claim and points to Graph instead when the user is in
        /// more groups than fit in the token.
        /// </summary>
        private static bool HasGroupsOverage(ClaimsIdentity identity) =>
            identity.HasClaim(x => x.Type == "hasgroups")
            || identity.HasClaim(x =>
                x.Type == "_claim_names" && x.Value.Contains("\"groups\"", StringComparison.Ordinal)
            );
    }
}
