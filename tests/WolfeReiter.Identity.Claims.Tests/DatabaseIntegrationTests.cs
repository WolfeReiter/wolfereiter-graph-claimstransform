using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    /// <summary>
    /// SqlServer and PgSql stores against real servers. Each scenario runs for SQL Server, for
    /// PostgreSQL with a static connection string, and for PostgreSQL with a per connection
    /// password provider as used for RDS IAM auth tokens.
    /// </summary>
    public class DatabaseIntegrationTests
    {
        const string Sql = TestDatabase.SqlServerVariable;
        const string Pg = TestDatabase.PgSqlVariable;

        static GraphUtilityServiceOptions Options(TestDatabase db, string table, bool connectionString, bool create = true) =>
            new()
            {
                GroupRoleMapStore = db.Kind,
                GroupRoleMapConnectionString = connectionString ? db.ConnectionString : null,
                GroupRoleMapTable = table,
                GroupRoleMapCreateTable = create,
            };

        static DbGroupRoleMapStore Store(GraphUtilityServiceOptions options, DbDataSource? appDataSource) =>
            (DbGroupRoleMapStore)
                WolfeReiterIdentityGroupWebAppClaimsBuilder.CreateStore(options, () => appDataSource);

        // ---- store from a connection string in configuration

        static async Task ConnectionStringStoreCreatesTableAndKeepsFirstWriter(TestDatabase db)
        {
            var table = db.NewTable();
            try
            {
                var first = Guid.NewGuid();
                await using (var store = Store(Options(db, table, connectionString: true), null))
                {
                    Assert.Empty(await store.LoadAsync(default));
                    await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = first }, default);
                }
                await using (var store = Store(Options(db, table, connectionString: true), null))
                {
                    await store.SaveAsync(
                        new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid(), ["Editors"] = Guid.NewGuid() },
                        default
                    );
                    var stored = await store.LoadAsync(default);
                    Assert.Equal(2, stored.Count);
                    Assert.Equal(first, stored["ADMINS"]);
                }
            }
            finally
            {
                await db.DropAsync(table);
            }
        }

        [DatabaseFact(Sql)] public Task SqlServer_ConnectionStringStore() => ConnectionStringStoreCreatesTableAndKeepsFirstWriter(TestDatabase.SqlServer());
        [DatabaseFact(Pg)] public Task PgSql_ConnectionStringStore() => ConnectionStringStoreCreatesTableAndKeepsFirstWriter(TestDatabase.PgSqlStatic());

        // ---- store on the app's registered data source

        static async Task AppDataSourceStoreCreatesTableAndLeavesSourceOpen(TestDatabase db)
        {
            var table = db.NewTable();
            await using var dataSource = db.AppDataSource();
            try
            {
                Assert.False(await db.TableExistsAsync(dataSource, table));
                var id = Guid.NewGuid();
                await using (var store = Store(Options(db, table, connectionString: false), dataSource))
                    await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = id }, default);

                Assert.True(await db.TableExistsAsync(dataSource, table));
                await using var store2 = Store(Options(db, table, connectionString: false), dataSource);
                Assert.Equal(id, (await store2.LoadAsync(default))["Admins"]);
                if (db.PasswordRequests != null)
                    Assert.True(db.PasswordRequests() > 0);
            }
            finally
            {
                await db.DropAsync(table);
            }
        }

        [DatabaseFact(Sql)] public Task SqlServer_AppDataSourceStore() => AppDataSourceStoreCreatesTableAndLeavesSourceOpen(TestDatabase.SqlServer());
        [DatabaseFact(Pg)] public Task PgSql_AppDataSourceStore_Static() => AppDataSourceStoreCreatesTableAndLeavesSourceOpen(TestDatabase.PgSqlStatic());
        [DatabaseFact(Pg)] public Task PgSql_AppDataSourceStore_PasswordProvider() => AppDataSourceStoreCreatesTableAndLeavesSourceOpen(TestDatabase.PgSqlPasswordProvider());

        // ---- table creation turned off

        static async Task WithoutCreateMissingTableFails(TestDatabase db)
        {
            await using var store = Store(Options(db, db.NewTable(), connectionString: true, create: false), null);
            await Assert.ThrowsAnyAsync<DbException>(() => store.LoadAsync(default));
        }

        [DatabaseFact(Sql)] public Task SqlServer_WithoutCreateMissingTableFails() => WithoutCreateMissingTableFails(TestDatabase.SqlServer());
        [DatabaseFact(Pg)] public Task PgSql_WithoutCreateMissingTableFails() => WithoutCreateMissingTableFails(TestDatabase.PgSqlStatic());

        static async Task WithoutCreateUsesExistingTable(TestDatabase db)
        {
            var table = db.NewTable();
            try
            {
                await using (var creator = Store(Options(db, table, connectionString: true), null))
                    await creator.LoadAsync(default);
                await using var store = Store(Options(db, table, connectionString: true, create: false), null);
                var id = Guid.NewGuid();
                await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = id }, default);
                Assert.Equal(id, (await store.LoadAsync(default))["Admins"]);
            }
            finally
            {
                await db.DropAsync(table);
            }
        }

        [DatabaseFact(Sql)] public Task SqlServer_WithoutCreateUsesExistingTable() => WithoutCreateUsesExistingTable(TestDatabase.SqlServer());
        [DatabaseFact(Pg)] public Task PgSql_WithoutCreateUsesExistingTable() => WithoutCreateUsesExistingTable(TestDatabase.PgSqlStatic());

        // ---- several instances starting at once

        static async Task ConcurrentInstancesAgreeOnFirstWriter(TestDatabase db)
        {
            var table = db.NewTable();
            await using var dataSource = db.AppDataSource();
            try
            {
                var stores = Enumerable.Range(0, 8)
                    .Select(_ => Store(Options(db, table, connectionString: false), dataSource))
                    .ToList();
                var ids = stores.Select(_ => Guid.NewGuid()).ToList();

                await Task.WhenAll(
                    stores.Select((store, i) =>
                        store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = ids[i] }, default)
                    )
                );

                var loaded = await Task.WhenAll(stores.Select(x => x.LoadAsync(default)));
                var winner = loaded[0]["Admins"];
                Assert.Contains(winner, ids);
                Assert.All(loaded, x => Assert.Equal(winner, Assert.Single(x).Value));
            }
            finally
            {
                await db.DropAsync(table);
            }
        }

        [DatabaseFact(Sql)] public Task SqlServer_ConcurrentInstances() => ConcurrentInstancesAgreeOnFirstWriter(TestDatabase.SqlServer());
        [DatabaseFact(Pg)] public Task PgSql_ConcurrentInstances() => ConcurrentInstancesAgreeOnFirstWriter(TestDatabase.PgSqlStatic());

        // ---- whole app: registration, startup discovery, restart

        static async Task<IReadOnlyDictionary<Guid, string>> LaunchAsync(
            TestDatabase db,
            string table,
            FakeGraph graph,
            DbDataSource? appDataSource
        )
        {
            var settings = new Dictionary<string, string?>
            {
                ["WolfeReiterIdentityClaims:GroupRoleMapStore"] = db.Kind.ToString(),
                ["WolfeReiterIdentityClaims:GroupRoleMapTable"] = table,
            };
            if (appDataSource == null)
                settings["WolfeReiterIdentityClaims:GroupRoleMapConnectionString"] = db.ConnectionString;

            var services = new ServiceCollection();
            services.AddLogging();
            if (appDataSource != null)
                services.AddSingleton(appDataSource);
            services.AddWolfeReiterAzureGroupsClaimsTransform(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
                new[] { "Admins" }
            );
            services.AddSingleton<IGraphUtilityService>(graph);
            await using var provider = services.BuildServiceProvider();

            Assert.IsType<DbGroupRoleMapStore>(provider.GetRequiredService<IGroupRoleMapStore>());
            foreach (var hosted in provider.GetServices<IHostedService>())
                await hosted.StartAsync(default);
            return await provider.GetRequiredService<GroupRoleMap>().GetAsync();
        }

        static async Task AppDiscoversOnceAndReusesAfterRestart(TestDatabase db, bool useAppDataSource)
        {
            var table = db.NewTable();
            await using var dataSource = db.AppDataSource();
            var appDataSource = useAppDataSource ? dataSource : null;
            try
            {
                var graph = new FakeGraph();
                var admins = graph.Add("Admins");
                Assert.False(await db.TableExistsAsync(dataSource, table));

                var first = await LaunchAsync(db, table, graph, appDataSource);
                Assert.Equal("Admins", first[Guid.Parse(admins.Id!)]);
                Assert.Equal(1, graph.Lookups);

                await using (var command = dataSource.CreateCommand(
                    $"SELECT ObjectId FROM {table} WHERE GroupName = 'Admins'"
                ))
                    Assert.Equal(admins.Id, Convert.ToString(await command.ExecuteScalarAsync())?.Trim());

                graph.Add("Admins");
                var second = await LaunchAsync(db, table, graph, appDataSource);
                Assert.Equal("Admins", second[Guid.Parse(admins.Id!)]);
                Assert.Equal(1, graph.Lookups);

                if (useAppDataSource && db.PasswordRequests != null)
                    Assert.True(db.PasswordRequests() > 0);
            }
            finally
            {
                await db.DropAsync(table);
            }
        }

        [DatabaseFact(Sql)] public Task SqlServer_App_ConnectionString() => AppDiscoversOnceAndReusesAfterRestart(TestDatabase.SqlServer(), false);
        [DatabaseFact(Sql)] public Task SqlServer_App_RegisteredDataSource() => AppDiscoversOnceAndReusesAfterRestart(TestDatabase.SqlServer(), true);
        [DatabaseFact(Pg)] public Task PgSql_App_ConnectionString() => AppDiscoversOnceAndReusesAfterRestart(TestDatabase.PgSqlStatic(), false);
        [DatabaseFact(Pg)] public Task PgSql_App_RegisteredDataSource_Static() => AppDiscoversOnceAndReusesAfterRestart(TestDatabase.PgSqlStatic(), true);
        [DatabaseFact(Pg)] public Task PgSql_App_RegisteredDataSource_PasswordProvider() => AppDiscoversOnceAndReusesAfterRestart(TestDatabase.PgSqlPasswordProvider(), true);
    }
}
