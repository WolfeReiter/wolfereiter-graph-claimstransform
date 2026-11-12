using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Identity.Web;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    /// <summary>Directory of groups that answers lookups by display name.</summary>
    class FakeGraph : IGraphUtilityService
    {
        public List<Group> Directory { get; } = new();
        public int Lookups;
        public TimeSpan Delay;

        public async Task<IEnumerable<Group>> SecurityGroupsByDisplayNameAsync(string displayName)
        {
            Interlocked.Increment(ref Lookups);
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay);
            return Directory
                .Where(x =>
                    string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
        }

        public static Group Group(string name, Guid? id = null) =>
            new()
            {
                Id = (id ?? Guid.NewGuid()).ToString(),
                DisplayName = name,
                SecurityEnabled = true,
                GroupTypes = new List<string>(),
            };

        public Group Add(string name)
        {
            var group = Group(name);
            Directory.Add(group);
            return group;
        }
    }

    /// <summary>Accepts saves and forgets them.</summary>
    class ForgetfulStore : IGroupRoleMapStore
    {
        public Task<IReadOnlyDictionary<string, Guid>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>());

        public Task SaveAsync(IReadOnlyDictionary<string, Guid> entries, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    /// <summary>Records log entries so tests can check their level.</summary>
    class ListLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    class ListLoggerFactory : Microsoft.Extensions.Logging.ILoggerFactory
    {
        public ListLogger<object> Logger { get; } = new();

        public void AddProvider(Microsoft.Extensions.Logging.ILoggerProvider provider) { }

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => Logger;

        public void Dispose() { }
    }

    static class Make
    {
        public static GroupRoleMap Map(
            IGraphUtilityService graph,
            IGroupRoleMapStore? store = null,
            Dictionary<string, string>? roles = null,
            params string[] names
        ) =>
            new(
                Options.Create(
                    new GraphUtilityServiceOptions
                    {
                        GroupRoles = roles ?? new(),
                        GroupNames = names.ToList(),
                    }
                ),
                graph,
                store ?? new MemoryGroupRoleMapStore(),
                NullLogger<GroupRoleMap>.Instance
            );

        public static ClaimsPrincipal Principal(params Claim[] claims) =>
            new(
                new ClaimsIdentity(
                    claims.Append(new Claim(ClaimConstants.ObjectId, Guid.NewGuid().ToString())),
                    "test"
                )
            );

        public static string TempPath(string extension) =>
            Path.Combine(Path.GetTempPath(), "grouprolemap-tests", $"{Guid.NewGuid():N}{extension}");
    }

    /// <summary>
    /// Runs only when the named environment variable holds a connection string to a test server.
    /// tests/run-database-tests.sh starts the servers and sets the variables.
    /// </summary>
    public sealed class DatabaseFactAttribute : FactAttribute
    {
        public DatabaseFactAttribute(string variable)
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
                Skip = $"{variable} is not set.";
        }
    }

    /// <summary>A database server under test and how the app would reach it.</summary>
    public sealed class TestDatabase
    {
        public const string SqlServerVariable = "GROUPROLEMAP_TEST_SQLSERVER";
        public const string PgSqlVariable = "GROUPROLEMAP_TEST_PGSQL";

        public required GroupRoleMapStoreKind Kind { get; init; }
        public required string ConnectionString { get; init; }
        public required string Schema { get; init; }

        /// <summary>Builds the data source the app registers as its default connection.</summary>
        public required Func<DbDataSource> AppDataSource { get; init; }

        /// <summary>Times the app data source was asked for a password, when it uses a provider.</summary>
        public Func<int>? PasswordRequests { get; init; }

        public string NewTable() => $"{Schema}.GroupRoleMap_{Guid.NewGuid():N}";

        public static TestDatabase SqlServer()
        {
            var cs = Environment.GetEnvironmentVariable(SqlServerVariable)!;
            return new()
            {
                Kind = GroupRoleMapStoreKind.SqlServer,
                ConnectionString = cs,
                Schema = "dbo",
                AppDataSource = () =>
                    Microsoft.Data.SqlClient.SqlClientFactory.Instance.CreateDataSource(cs),
            };
        }

        /// <summary>PostgreSQL with the password in the connection string.</summary>
        public static TestDatabase PgSqlStatic()
        {
            var cs = Environment.GetEnvironmentVariable(PgSqlVariable)!;
            return new()
            {
                Kind = GroupRoleMapStoreKind.PgSql,
                ConnectionString = cs,
                Schema = "public",
                AppDataSource = () => Npgsql.NpgsqlDataSource.Create(cs),
            };
        }

        /// <summary>
        /// PostgreSQL whose connection string has no password. The password is supplied per
        /// connection by a periodic provider, the way an RDS IAM auth token is.
        /// </summary>
        public static TestDatabase PgSqlPasswordProvider()
        {
            var cs = Environment.GetEnvironmentVariable(PgSqlVariable)!;
            var calls = 0;
            return new()
            {
                Kind = GroupRoleMapStoreKind.PgSql,
                ConnectionString = cs,
                Schema = "public",
                PasswordRequests = () => calls,
                AppDataSource = () =>
                {
                    var settings = new Npgsql.NpgsqlConnectionStringBuilder(cs);
                    var password = settings.Password!;
                    settings.Password = null;
                    var builder = new Npgsql.NpgsqlDataSourceBuilder(settings.ConnectionString);
                    builder.UsePeriodicPasswordProvider(
                        (_, _) =>
                        {
                            Interlocked.Increment(ref calls);
                            return ValueTask.FromResult(password);
                        },
                        TimeSpan.FromMinutes(10),
                        TimeSpan.FromSeconds(10)
                    );
                    return builder.Build();
                },
            };
        }

        public async Task<bool> TableExistsAsync(DbDataSource dataSource, string table)
        {
            try
            {
                await using var command = dataSource.CreateCommand($"SELECT COUNT(*) FROM {table}");
                await command.ExecuteScalarAsync();
                return true;
            }
            catch (DbException)
            {
                return false;
            }
        }

        public async Task DropAsync(string table)
        {
            await using var dataSource = AppDataSource();
            await using var command = dataSource.CreateCommand($"DROP TABLE IF EXISTS {table}");
            await command.ExecuteNonQueryAsync();
        }
    }
}
