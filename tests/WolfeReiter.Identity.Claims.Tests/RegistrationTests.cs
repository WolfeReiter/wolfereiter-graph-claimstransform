using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    public class RegistrationTests
    {
        static IConfiguration Config(Dictionary<string, string?>? values = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build();

        static ServiceCollection Services(IConfiguration config, string[]? names = null, FakeGraph? graph = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWolfeReiterAzureGroupsClaimsTransform(config, names);
            if (graph != null)
                services.AddSingleton<IGraphUtilityService>(graph);
            return services;
        }

        [Fact]
        public void BindsBothConfigurationSections()
        {
            var id = Guid.NewGuid().ToString();
            var config = Config(
                new()
                {
                    ["AzureAD:TenantId"] = "tenant",
                    ["AzureAD:ClientId"] = "client",
                    ["AzureAD:ClientSecret"] = "secret",
                    [$"WolfeReiterIdentityClaims:GroupRoles:{id}"] = "Admins",
                    ["WolfeReiterIdentityClaims:GroupNames:0"] = "Editors",
                    ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = "PgSql",
                    ["WolfeReiterIdentityClaims:GroupRoleMapConnectionString"] = "Host=x",
                    ["WolfeReiterIdentityClaims:GroupRoleMapTable"] = "app.map",
                    ["WolfeReiterIdentityClaims:GroupRoleMapCreateTable"] = "false",
                    ["WolfeReiterIdentityClaims:GroupRoleMapJsonPath"] = "map.json",
                }
            );
            using var provider = Services(config).BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value;

            Assert.Equal("tenant", options.TenantId);
            Assert.Equal("client", options.ClientId);
            Assert.Equal("secret", options.ClientSecret);
            Assert.Equal("Admins", options.GroupRoles[id]);
            Assert.Equal(new[] { "Editors" }, options.GroupNames);
            Assert.Equal(GroupRoleMapStoreKind.PgSql, options.GroupRoleMapStore);
            Assert.Equal("Host=x", options.GroupRoleMapConnectionString);
            Assert.Equal("app.map", options.GroupRoleMapTable);
            Assert.False(options.GroupRoleMapCreateTable);
            Assert.Equal("map.json", options.GroupRoleMapJsonPath);
        }

        [Fact]
        public void DefaultsWhenNothingIsConfigured()
        {
            using var provider = Services(Config()).BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value;

            Assert.Empty(options.GroupRoles);
            Assert.Empty(options.GroupNames);
            Assert.Null(options.GroupRoleMapStore);
            Assert.Equal("GroupRoleMap", options.GroupRoleMapTable);
            Assert.True(options.GroupRoleMapCreateTable);
            Assert.IsType<MemoryGroupRoleMapStore>(provider.GetRequiredService<IGroupRoleMapStore>());
        }

        [Fact]
        public void StoreKindNameIsCaseInsensitive()
        {
            using var provider = Services(
                    Config(new() { ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = "pgsql" })
                )
                .BuildServiceProvider();
            Assert.Equal(
                GroupRoleMapStoreKind.PgSql,
                provider.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value.GroupRoleMapStore
            );
        }

        [Fact]
        public void UnknownStoreKindFails()
        {
            using var provider = Services(
                    Config(new() { ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = "Postgres" })
                )
                .BuildServiceProvider();
            Assert.ThrowsAny<InvalidOperationException>(() =>
                provider.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value
            );
        }

        [Fact]
        public void NamesInCodeAreAddedToConfiguredNamesOnce()
        {
            var config = Config(
                new()
                {
                    ["WolfeReiterIdentityClaims:GroupNames:0"] = "Editors",
                    ["WolfeReiterIdentityClaims:GroupNames:1"] = "Admins",
                }
            );
            using var provider = Services(config, new[] { "admins", "Auditors" }).BuildServiceProvider();
            var names = provider.GetRequiredService<IOptions<GraphUtilityServiceOptions>>().Value.GroupNames;

            Assert.Equal(new[] { "Editors", "Admins", "Auditors" }, names);
        }

        [Fact]
        public void RegistersTransformMapStartupAndGraph()
        {
            var services = Services(Config());

            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IClaimsTransformation)
                    && x.ImplementationType == typeof(AzureGroupsClaimsTransform)
                    && x.Lifetime == ServiceLifetime.Scoped
            );
            Assert.Contains(
                services,
                x => x.ServiceType == typeof(GroupRoleMap) && x.Lifetime == ServiceLifetime.Singleton
            );
            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType == typeof(GroupRoleMapStartup)
            );
            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IGraphUtilityService)
                    && x.ImplementationType == typeof(GraphUtilityService)
            );
        }

        [Fact]
        public void GenericOverloadRegistersCustomGraphService()
        {
            var services = new ServiceCollection();
            services.AddWolfeReiterAzureGroupsClaimsTransform<FakeGraph>(Config());

            Assert.Contains(
                services,
                x => x.ServiceType == typeof(IGraphUtilityService) && x.ImplementationType == typeof(FakeGraph)
            );
            Assert.Contains(services, x => x.ServiceType == typeof(FakeGraph));
        }

        [Fact]
        public void StoreRegisteredEarlierWins()
        {
            var services = new ServiceCollection();
            var custom = new ForgetfulStore();
            services.AddSingleton<IGroupRoleMapStore>(custom);
            services.AddWolfeReiterAzureGroupsClaimsTransform(
                Config(new() { ["WolfeReiterIdentityClaims:GroupRoleMapJsonPath"] = "map.json" })
            );
            using var provider = services.BuildServiceProvider();
            Assert.Same(custom, provider.GetRequiredService<IGroupRoleMapStore>());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DbStoreExtensionWinsBeforeOrAfter(bool before)
        {
            var services = new ServiceCollection();
            await using var dataSource = SqliteFactory.Instance.CreateDataSource("Data Source=:memory:");
            if (before)
                services.AddWolfeReiterGroupRoleMapDbStore(dataSource);
            services.AddWolfeReiterAzureGroupsClaimsTransform(Config());
            if (!before)
                services.AddWolfeReiterGroupRoleMapDbStore(dataSource);

            Assert.Single(services, x => x.ServiceType == typeof(IGroupRoleMapStore));
            await using var provider = services.BuildServiceProvider();
            Assert.IsType<DbGroupRoleMapStore>(provider.GetRequiredService<IGroupRoleMapStore>());
        }

        [Fact]
        public async Task NamesInCodeAreDiscoveredOnceAtStartup()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            await using var provider = Services(Config(), new[] { "Admins" }, graph).BuildServiceProvider();

            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(default);
            var map = await provider.GetRequiredService<GroupRoleMap>().GetAsync();

            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
            Assert.IsType<MemoryGroupRoleMapStore>(provider.GetRequiredService<IGroupRoleMapStore>());
            Assert.Equal(1, graph.Lookups);
        }

        [Theory]
        [InlineData("PgSql")]
        [InlineData("SqlServer")]
        [InlineData("Json")]
        public async Task StaticRolesInSettingsNeedNoStore(string kind)
        {
            // the store setting is incomplete and would throw if the store were ever created
            var id = Guid.NewGuid();
            var graph = new FakeGraph();
            await using var provider = Services(
                    Config(
                        new()
                        {
                            [$"WolfeReiterIdentityClaims:GroupRoles:{id}"] = "Admins",
                            ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = kind,
                        }
                    ),
                    null,
                    graph
                )
                .BuildServiceProvider();

            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(default);
            var map = await provider.GetRequiredService<GroupRoleMap>().GetAsync();

            Assert.Equal("Admins", map[id]);
            Assert.Equal(0, graph.Lookups);
            Assert.Throws<GroupRoleMapException>(() => provider.GetRequiredService<IGroupRoleMapStore>());
        }

        [Fact]
        public async Task TransformResolvedFromContainerMapsRoles()
        {
            var id = Guid.NewGuid();
            await using var provider = Services(
                    Config(new() { [$"WolfeReiterIdentityClaims:GroupRoles:{id}"] = "Admins" })
                )
                .BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var transform = scope.ServiceProvider.GetRequiredService<IClaimsTransformation>();

            var result = await transform.TransformAsync(
                Make.Principal(new System.Security.Claims.Claim("groups", id.ToString()))
            );
            Assert.True(result.IsInRole("Admins"));
        }
    }

    public class StoreSelectionTests
    {
        static IGroupRoleMapStore Create(GraphUtilityServiceOptions options, DbDataSource? dataSource = null) =>
            WolfeReiterIdentityGroupWebAppClaimsBuilder.CreateStore(options, () => dataSource);

        [Fact]
        public void DefaultsToMemory() =>
            Assert.IsType<MemoryGroupRoleMapStore>(Create(new GraphUtilityServiceOptions()));

        [Fact]
        public void JsonPathAloneSelectsJson() =>
            Assert.IsType<JsonFileGroupRoleMapStore>(
                Create(new GraphUtilityServiceOptions { GroupRoleMapJsonPath = "map.json" })
            );

        [Fact]
        public void ExplicitMemoryIgnoresJsonPath() =>
            Assert.IsType<MemoryGroupRoleMapStore>(
                Create(
                    new GraphUtilityServiceOptions
                    {
                        GroupRoleMapStore = GroupRoleMapStoreKind.Memory,
                        GroupRoleMapJsonPath = "map.json",
                    }
                )
            );

        [Fact]
        public void JsonWithoutPathFails() =>
            Assert.Throws<GroupRoleMapException>(() =>
                Create(new GraphUtilityServiceOptions { GroupRoleMapStore = GroupRoleMapStoreKind.Json })
            );

        [Theory]
        [InlineData(GroupRoleMapStoreKind.SqlServer, "Server=localhost;Database=x;User Id=u;Password=p")]
        [InlineData(GroupRoleMapStoreKind.PgSql, "Host=localhost;Database=x;Username=u;Password=p")]
        public async Task DatabaseKindWithConnectionStringOwnsItsDataSource(
            GroupRoleMapStoreKind kind,
            string connectionString
        )
        {
            await using var unused = SqliteFactory.Instance.CreateDataSource("Data Source=:memory:");
            var store = Create(
                new GraphUtilityServiceOptions
                {
                    GroupRoleMapStore = kind,
                    GroupRoleMapConnectionString = connectionString,
                },
                unused
            );
            await Assert.IsType<DbGroupRoleMapStore>(store).DisposeAsync();
        }

        [Theory]
        [InlineData(GroupRoleMapStoreKind.SqlServer)]
        [InlineData(GroupRoleMapStoreKind.PgSql)]
        public void DatabaseKindWithoutConnectionStringOrDataSourceFails(GroupRoleMapStoreKind kind)
        {
            var ex = Assert.Throws<GroupRoleMapException>(() =>
                Create(new GraphUtilityServiceOptions { GroupRoleMapStore = kind })
            );
            Assert.Contains("DbDataSource", ex.Message);
        }

        [Theory]
        [InlineData(GroupRoleMapStoreKind.SqlServer)]
        [InlineData(GroupRoleMapStoreKind.PgSql)]
        public void DatabaseKindRejectsUnsafeTableName(GroupRoleMapStoreKind kind) =>
            Assert.Throws<ArgumentException>(() =>
                Create(
                    new GraphUtilityServiceOptions
                    {
                        GroupRoleMapStore = kind,
                        GroupRoleMapConnectionString = "Host=x",
                        GroupRoleMapTable = "x; DROP TABLE y",
                    }
                )
            );

        [Fact]
        public async Task DatabaseKindWithoutConnectionStringUsesRegisteredDataSource()
        {
            var cs = $"Data Source=grouprolemap-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            using var keepAlive = new SqliteConnection(cs);
            keepAlive.Open();
            await using var dataSource = SqliteFactory.Instance.CreateDataSource(cs);

            var services = new ServiceCollection();
            services.AddSingleton<DbDataSource>(dataSource);
            services.AddWolfeReiterAzureGroupsClaimsTransform(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?> { ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = "PgSql" }
                    )
                    .Build()
            );
            await using (var provider = services.BuildServiceProvider())
            {
                var store = provider.GetRequiredService<IGroupRoleMapStore>();
                var id = Guid.NewGuid();
                await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = id }, default);
                Assert.Equal(id, (await store.LoadAsync(default))["Admins"]);
            }

            // the container disposed the store, and the app data source is still usable
            await using var command = dataSource.CreateCommand("SELECT COUNT(*) FROM GroupRoleMap");
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
    }

    public class StartupTests
    {
        static GroupRoleMapStartup Startup(GroupRoleMap map) =>
            new(map, NullLogger<GroupRoleMapStartup>.Instance);

        [Fact]
        public async Task FailsWithoutMapping() =>
            await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Startup(Make.Map(new FakeGraph())).StartAsync(default)
            );

        [Fact]
        public async Task FailsOnDuplicateName()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            graph.Add("Admins");
            await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Startup(Make.Map(graph, null, null, "Admins")).StartAsync(default)
            );
        }

        [Fact]
        public async Task BuildsMapBeforeRequests()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var map = Make.Map(graph, null, null, "Admins");

            await Startup(map).StartAsync(default);
            Assert.Equal(1, graph.Lookups);

            await map.GetAsync();
            Assert.Equal(1, graph.Lookups);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public async Task NoMatchOrSeveralMatchesIsLoggedCriticalAndThrown(int matches)
        {
            var graph = new FakeGraph();
            for (var i = 0; i < matches; i++)
                graph.Add("Admins");
            var logger = new ListLogger<GroupRoleMapStartup>();

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                new GroupRoleMapStartup(Make.Map(graph, null, null, "Admins"), logger).StartAsync(default)
            );
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Critical, entry.Level);
            Assert.Same(ex, entry.Exception);
        }

        [Fact]
        public async Task OtherStartupFailuresAreLoggedCriticalAndThrown()
        {
            var logger = new ListLogger<GroupRoleMapStartup>();
            var store = new ThrowingStore();
            await Assert.ThrowsAsync<TimeoutException>(() =>
                new GroupRoleMapStartup(Make.Map(new FakeGraph(), store, null, "Admins"), logger).StartAsync(default)
            );
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Critical, Assert.Single(logger.Entries).Level);
        }

        class ThrowingStore : IGroupRoleMapStore
        {
            public Task<IReadOnlyDictionary<string, Guid>> LoadAsync(System.Threading.CancellationToken cancellationToken) =>
                throw new TimeoutException("database unreachable");

            public Task SaveAsync(IReadOnlyDictionary<string, Guid> entries, System.Threading.CancellationToken cancellationToken) =>
                throw new TimeoutException("database unreachable");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public async Task HostDoesNotStartOnNoMatchOrSeveralMatches(int matches)
        {
            var graph = new FakeGraph();
            for (var i = 0; i < matches; i++)
                graph.Add("Admins");
            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddWolfeReiterAzureGroupsClaimsTransform(builder.Configuration, new[] { "Admins" });
            builder.Services.AddSingleton<IGraphUtilityService>(graph);
            using var host = builder.Build();

            await Assert.ThrowsAsync<GroupRoleMapException>(() => host.StartAsync());
        }

        [Fact]
        public async Task StopDoesNothing() =>
            await Startup(Make.Map(new FakeGraph())).StopAsync(default);
    }
}
