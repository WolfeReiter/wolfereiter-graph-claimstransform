using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Caching.Distributed;

namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityServiceOptions
    {
        //public string GraphApiVersion { get; set; } = "https://graph.microsoft.com/v1.0";
        public string GraphEndpoint { get; set; } = "https://graph.microsoft.com/.default";
        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public int GroupClaimsCacheAbsoluteExpirationHours { get; set; }

        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public int GroupClaimsCacheSlidingExpirationMinutes { get; set; }

        [Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
        public DistributedCacheEntryOptions DistributedCacheEntryOptions =>
            throw new NotSupportedException("Role claims come from the token groups claim and are no longer cached per user.");

        public string Scheme = OpenIdConnectDefaults.AuthenticationScheme;

        public string? TenantId { get; set; }
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Static map of security group Object ID to role claim value. Only groups listed here or
        /// in <see cref="GroupNames"/> produce role claims, and at least one must be set.
        /// </summary>
        public Dictionary<string, string> GroupRoles { get; set; } = new();

        /// <summary>
        /// Security group display names to resolve to Object IDs at startup. Each name must match
        /// exactly one security group, otherwise startup fails with <see cref="GroupRoleMapException"/>.
        /// The resolved Object IDs are kept by the registered <see cref="IGroupRoleMapStore"/> so
        /// later launches use the same groups even if a duplicate name appears.
        /// </summary>
        public List<string> GroupNames { get; set; } = new();

        /// <summary>
        /// Where discovered Object IDs are kept. When unset, Json is used if
        /// <see cref="GroupRoleMapJsonPath"/> is set, otherwise Memory.
        /// </summary>
        public GroupRoleMapStoreKind? GroupRoleMapStore { get; set; }

        /// <summary>File for the Json store.</summary>
        public string? GroupRoleMapJsonPath { get; set; }

        /// <summary>
        /// Connection string for the SqlServer and PgSql stores. When empty, the store uses the
        /// <see cref="System.Data.Common.DbDataSource"/> registered in the service collection, so
        /// it signs in the same way as the rest of the app.
        /// </summary>
        public string? GroupRoleMapConnectionString { get; set; }

        /// <summary>Table for the SqlServer and PgSql stores, optionally schema qualified.</summary>
        public string GroupRoleMapTable { get; set; } = "GroupRoleMap";

        /// <summary>Create the SqlServer or PgSql table on first use when it does not exist.</summary>
        public bool GroupRoleMapCreateTable { get; set; } = true;
    }

    public enum GroupRoleMapStoreKind
    {
        Memory,
        Json,
        SqlServer,
        PgSql,
    }
}
