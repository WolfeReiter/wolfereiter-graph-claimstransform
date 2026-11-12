using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// Keeps the group display name to Object ID pairs found by discovery so the mapping is
    /// fixed after the first launch.
    /// </summary>
    public interface IGroupRoleMapStore
    {
        /// <summary>All stored pairs, keyed by group name.</summary>
        Task<IReadOnlyDictionary<string, Guid>> LoadAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Adds pairs for names not already stored. A name that is already stored keeps its
        /// existing Object ID.
        /// </summary>
        Task SaveAsync(IReadOnlyDictionary<string, Guid> entries, CancellationToken cancellationToken);
    }

    /// <summary>Keeps discovered pairs for the life of the process.</summary>
    public class MemoryGroupRoleMapStore : IGroupRoleMapStore
    {
        private readonly ConcurrentDictionary<string, Guid> Entries =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyDictionary<string, Guid>> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, Guid>>(
                new Dictionary<string, Guid>(Entries, StringComparer.OrdinalIgnoreCase)
            );

        public Task SaveAsync(
            IReadOnlyDictionary<string, Guid> entries,
            CancellationToken cancellationToken
        )
        {
            foreach (var (name, id) in entries)
                Entries.TryAdd(name, id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Keeps discovered pairs in a JSON object of name to Object ID. Meant for a single
    /// instance, since the file is not locked across processes.
    /// </summary>
    public class JsonFileGroupRoleMapStore : IGroupRoleMapStore
    {
        private readonly string Path;
        private readonly SemaphoreSlim Gate = new(1, 1);

        public JsonFileGroupRoleMapStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A file path is required.", nameof(path));
            Path = System.IO.Path.GetFullPath(path);
        }

        public async Task<IReadOnlyDictionary<string, Guid>> LoadAsync(
            CancellationToken cancellationToken
        )
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                return await ReadAsync(cancellationToken);
            }
            finally
            {
                Gate.Release();
            }
        }

        public async Task SaveAsync(
            IReadOnlyDictionary<string, Guid> entries,
            CancellationToken cancellationToken
        )
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var current = await ReadAsync(cancellationToken);
                var changed = false;
                foreach (var (name, id) in entries)
                    changed |= current.TryAdd(name, id);
                if (!changed)
                    return;

                var directory = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var temp = Path + ".tmp";
                var fileOptions = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                };
                if (!OperatingSystem.IsWindows())
                    fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                // a leftover temp file keeps its old mode, so start fresh
                File.Delete(temp);
                await using (var stream = new FileStream(temp, fileOptions))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        current,
                        new JsonSerializerOptions { WriteIndented = true },
                        cancellationToken
                    );
                }
                File.Move(temp, Path, overwrite: true);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<Dictionary<string, Guid>> ReadAsync(CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(Path))
                return result;

            await using var stream = File.OpenRead(Path);
            var stored = await JsonSerializer.DeserializeAsync<Dictionary<string, Guid>>(
                stream,
                cancellationToken: cancellationToken
            );
            if (stored != null)
            {
                foreach (var (name, id) in stored)
                {
                    if (!result.TryAdd(name, id))
                        throw new GroupRoleMapException(
                            $"Group role map file {Path} lists the group name '{name}' more than once."
                        );
                }
            }
            return result;
        }
    }

    /// <summary>
    /// Keeps discovered pairs in a database table through any ADO.NET provider that accepts
    /// named <c>@</c> parameters. <see cref="SqlServer"/> and <see cref="PgSql"/> build a
    /// store that creates its table on first use.
    /// <code>
    /// CREATE TABLE GroupRoleMap (
    ///     GroupName varchar(256) NOT NULL PRIMARY KEY,
    ///     ObjectId  char(36)     NOT NULL
    /// );
    /// </code>
    /// </summary>
    public class DbGroupRoleMapStore : IGroupRoleMapStore, IAsyncDisposable
    {
        private static readonly Regex TableNamePattern = new(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$",
            RegexOptions.CultureInvariant
        );

        private readonly DbDataSource DataSource;
        private readonly bool OwnsDataSource;
        private readonly string Table;
        private readonly string? CreateTableSql;
        private readonly SemaphoreSlim CreateGate = new(1, 1);
        private bool TableReady;

        /// <param name="dataSource">Data source for the table. It is not disposed by this store.</param>
        /// <param name="tableName">Table name, optionally schema qualified.</param>
        /// <param name="createTableSql">Statement run once before first use to create the table, or null to skip.</param>
        public DbGroupRoleMapStore(
            DbDataSource dataSource,
            string tableName = "GroupRoleMap",
            string? createTableSql = null
        )
            : this(dataSource, tableName, createTableSql, ownsDataSource: false) { }

        private DbGroupRoleMapStore(
            DbDataSource dataSource,
            string tableName,
            string? createTableSql,
            bool ownsDataSource
        )
        {
            DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            Table = ValidateTableName(tableName);
            CreateTableSql = createTableSql;
            OwnsDataSource = ownsDataSource;
            TableReady = createTableSql == null;
        }

        /// <summary>SQL Server store through Microsoft.Data.SqlClient.</summary>
        public static DbGroupRoleMapStore SqlServer(
            string connectionString,
            string tableName = "GroupRoleMap",
            bool createTable = true
        ) =>
            new(
                Microsoft.Data.SqlClient.SqlClientFactory.Instance.CreateDataSource(
                    RequireConnectionString(connectionString)
                ),
                ValidateTableName(tableName),
                createTable ? SqlServerCreateTable(tableName) : null,
                ownsDataSource: true
            );

        /// <summary>
        /// SQL Server store on a data source the app already uses, such as the one registered
        /// as <see cref="DbDataSource"/>. The data source is not disposed by this store.
        /// </summary>
        public static DbGroupRoleMapStore SqlServer(
            DbDataSource dataSource,
            string tableName = "GroupRoleMap",
            bool createTable = true
        ) =>
            new(
                dataSource,
                ValidateTableName(tableName),
                createTable ? SqlServerCreateTable(tableName) : null,
                ownsDataSource: false
            );

        /// <summary>PostgreSQL store through Npgsql.</summary>
        public static DbGroupRoleMapStore PgSql(
            string connectionString,
            string tableName = "GroupRoleMap",
            bool createTable = true
        ) =>
            new(
                Npgsql.NpgsqlDataSource.Create(RequireConnectionString(connectionString)),
                ValidateTableName(tableName),
                createTable ? PgSqlCreateTable(tableName) : null,
                ownsDataSource: true
            );

        /// <summary>
        /// PostgreSQL store on a data source the app already uses, such as an NpgsqlDataSource
        /// that signs in with IAM auth tokens. The data source is not disposed by this store.
        /// </summary>
        public static DbGroupRoleMapStore PgSql(
            DbDataSource dataSource,
            string tableName = "GroupRoleMap",
            bool createTable = true
        ) =>
            new(
                dataSource,
                ValidateTableName(tableName),
                createTable ? PgSqlCreateTable(tableName) : null,
                ownsDataSource: false
            );

        private static string SqlServerCreateTable(string table) =>
            $"IF OBJECT_ID(N'{table}', N'U') IS NULL CREATE TABLE {table} "
            + "(GroupName nvarchar(256) NOT NULL PRIMARY KEY, ObjectId char(36) NOT NULL)";

        private static string PgSqlCreateTable(string table) =>
            $"CREATE TABLE IF NOT EXISTS {table} "
            + "(GroupName varchar(256) NOT NULL PRIMARY KEY, ObjectId char(36) NOT NULL)";

        private static string ValidateTableName(string tableName)
        {
            if (tableName == null || !TableNamePattern.IsMatch(tableName))
                throw new ArgumentException(
                    "Table name must be a plain identifier, optionally schema qualified.",
                    nameof(tableName)
                );
            return tableName;
        }

        private static string RequireConnectionString(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new GroupRoleMapException(
                    "GroupRoleMapConnectionString is empty."
                );
            return connectionString;
        }

        private async Task EnsureTableAsync(CancellationToken cancellationToken)
        {
            if (TableReady)
                return;
            await CreateGate.WaitAsync(cancellationToken);
            try
            {
                if (TableReady)
                    return;
                try
                {
                    await using var create = DataSource.CreateCommand(CreateTableSql);
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (DbException)
                {
                    // another instance may have created it at the same moment
                    await using var probe = DataSource.CreateCommand(
                        $"SELECT COUNT(*) FROM {Table}"
                    );
                    await probe.ExecuteScalarAsync(cancellationToken);
                }
                TableReady = true;
            }
            finally
            {
                CreateGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (OwnsDataSource)
                await DataSource.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        public async Task<IReadOnlyDictionary<string, Guid>> LoadAsync(
            CancellationToken cancellationToken
        )
        {
            await EnsureTableAsync(cancellationToken);
            var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            await using var command = DataSource.CreateCommand(
                $"SELECT GroupName, ObjectId FROM {Table}"
            );
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                var value = Convert.ToString(reader.GetValue(1));
                if (!Guid.TryParse(value, out var id))
                    throw new GroupRoleMapException(
                        $"Table {Table} has an invalid Object ID '{value}' for group '{name}'."
                    );
                if (!result.TryAdd(name, id))
                    throw new GroupRoleMapException(
                        $"Table {Table} lists the group name '{name}' more than once."
                    );
            }
            return result;
        }

        public async Task SaveAsync(
            IReadOnlyDictionary<string, Guid> entries,
            CancellationToken cancellationToken
        )
        {
            await EnsureTableAsync(cancellationToken);
            await using var connection = await DataSource.OpenConnectionAsync(cancellationToken);
            foreach (var (name, id) in entries)
            {
                try
                {
                    await using var insert = connection.CreateCommand();
                    insert.CommandText =
                        $"INSERT INTO {Table} (GroupName, ObjectId) SELECT @name, @oid "
                        + $"WHERE NOT EXISTS (SELECT 1 FROM {Table} WHERE GroupName = @name2)";
                    AddParameter(insert, "@name", name);
                    AddParameter(insert, "@oid", id.ToString("D"));
                    AddParameter(insert, "@name2", name);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (DbException)
                {
                    // another instance stored this name first, and its value wins
                    if (!await ExistsAsync(connection, name, cancellationToken))
                        throw;
                }
            }
        }

        private async Task<bool> ExistsAsync(
            DbConnection connection,
            string name,
            CancellationToken cancellationToken
        )
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT COUNT(*) FROM {Table} WHERE GroupName = @name";
            AddParameter(select, "@name", name);
            return Convert.ToInt64(await select.ExecuteScalarAsync(cancellationToken)) > 0;
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
