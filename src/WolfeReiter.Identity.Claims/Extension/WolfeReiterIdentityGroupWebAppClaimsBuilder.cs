using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WolfeReiter.Identity.Claims;

namespace Microsoft.Identity.Web
{
    public static class WolfeReiterIdentityGroupWebAppClaimsBuilder
    {
        /// <param name="groupNames">
        /// Expected security group names, added to any GroupNames in configuration. They are
        /// resolved to Object IDs once at startup and kept by the configured store, in memory
        /// when none is configured.
        /// </param>
        public static void AddWolfeReiterAzureGroupsClaimsTransform(
            this IServiceCollection services,
            IConfiguration configuration,
            IEnumerable<string>? groupNames = null
        )
        {
            AddCore(services, configuration, groupNames);
            services.AddSingleton<IGraphUtilityService, GraphUtilityService>();
        }

        public static void AddWolfeReiterAzureGroupsClaimsTransform<TGraphUtilityService>(
            this IServiceCollection services,
            IConfiguration configuration,
            IEnumerable<string>? groupNames = null
        )
            where TGraphUtilityService : class, IGraphUtilityService
        {
            AddCore(services, configuration, groupNames);
            services.AddSingleton<IGraphUtilityService, TGraphUtilityService>();
            //add TGraphUtilityService as TGraphUtilityService so that a ctor service injection reference
            //does not require casting IGraphUtilityService to TGraphUtilityService
            services.AddSingleton<TGraphUtilityService>();
        }

        /// <summary>
        /// Keeps discovered group Object IDs in a database table. Works whether called before or
        /// after AddWolfeReiterAzureGroupsClaimsTransform. See <see cref="DbGroupRoleMapStore"/>
        /// for the table definition.
        /// </summary>
        public static IServiceCollection AddWolfeReiterGroupRoleMapDbStore(
            this IServiceCollection services,
            DbDataSource dataSource,
            string tableName = "GroupRoleMap"
        )
        {
            services.Replace(
                ServiceDescriptor.Singleton<IGroupRoleMapStore>(
                    new DbGroupRoleMapStore(dataSource, tableName)
                )
            );
            return services;
        }

        private static void AddCore(
            IServiceCollection services,
            IConfiguration configuration,
            IEnumerable<string>? groupNames
        )
        {
            services
                .Configure<GraphUtilityServiceOptions>(
                    configuration.GetSection("WolfeReiterIdentityClaims")
                )
                .AddOptions<GraphUtilityServiceOptions>()
                .Bind(configuration.GetSection("AzureAD"));

            var names = groupNames?.ToList();
            if (names?.Count > 0)
            {
                services.PostConfigure<GraphUtilityServiceOptions>(options =>
                {
                    foreach (var name in names)
                    {
                        if (!options.GroupNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                            options.GroupNames.Add(name);
                    }
                });
            }

            //store chosen by GroupRoleMapStore. A store registered earlier wins.
            services.TryAddSingleton<IGroupRoleMapStore>(sp =>
                CreateStore(
                    sp.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value,
                    sp.GetService<DbDataSource>
                )
            );
            services.TryAddSingleton(sp => new GroupRoleMap(
                sp.GetRequiredService<IOptions<GraphUtilityServiceOptions>>(),
                sp.GetRequiredService<IGraphUtilityService>(),
                () => sp.GetRequiredService<IGroupRoleMapStore>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GroupRoleMap>>()
            ));
            services.AddHostedService<GroupRoleMapStartup>();
            services.AddScoped<IClaimsTransformation, AzureGroupsClaimsTransform>();
        }

        internal static IGroupRoleMapStore CreateStore(
            GraphUtilityServiceOptions options,
            Func<DbDataSource?> defaultDataSource
        )
        {
            var kind =
                options.GroupRoleMapStore
                ?? (
                    string.IsNullOrWhiteSpace(options.GroupRoleMapJsonPath)
                        ? GroupRoleMapStoreKind.Memory
                        : GroupRoleMapStoreKind.Json
                );
            var connectionString = options.GroupRoleMapConnectionString;
            var useDefault = string.IsNullOrWhiteSpace(connectionString);
            DbDataSource DefaultDataSource() =>
                defaultDataSource()
                ?? throw new GroupRoleMapException(
                    $"The {kind} group role map store needs GroupRoleMapConnectionString or a DbDataSource registered in the service collection."
                );

            return kind switch
            {
                GroupRoleMapStoreKind.Memory => new MemoryGroupRoleMapStore(),
                GroupRoleMapStoreKind.Json => string.IsNullOrWhiteSpace(options.GroupRoleMapJsonPath)
                    ? throw new GroupRoleMapException(
                        "GroupRoleMapJsonPath is required for the Json group role map store."
                    )
                    : new JsonFileGroupRoleMapStore(options.GroupRoleMapJsonPath),
                GroupRoleMapStoreKind.SqlServer => useDefault
                    ? DbGroupRoleMapStore.SqlServer(
                        DefaultDataSource(),
                        options.GroupRoleMapTable,
                        options.GroupRoleMapCreateTable
                    )
                    : DbGroupRoleMapStore.SqlServer(
                        connectionString!,
                        options.GroupRoleMapTable,
                        options.GroupRoleMapCreateTable
                    ),
                GroupRoleMapStoreKind.PgSql => useDefault
                    ? DbGroupRoleMapStore.PgSql(
                        DefaultDataSource(),
                        options.GroupRoleMapTable,
                        options.GroupRoleMapCreateTable
                    )
                    : DbGroupRoleMapStore.PgSql(
                        connectionString!,
                        options.GroupRoleMapTable,
                        options.GroupRoleMapCreateTable
                    ),
                _ => throw new GroupRoleMapException($"Unknown group role map store {kind}."),
            };
        }
    }
}
