using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    public class MemoryStoreTests
    {
        [Fact]
        public async Task StartsEmpty() =>
            Assert.Empty(await new MemoryGroupRoleMapStore().LoadAsync(default));

        [Fact]
        public async Task FirstWriterWinsIgnoringCase()
        {
            var store = new MemoryGroupRoleMapStore();
            var first = Guid.NewGuid();
            await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = first }, default);
            await store.SaveAsync(new Dictionary<string, Guid> { ["ADMINS"] = Guid.NewGuid() }, default);

            var stored = await store.LoadAsync(default);
            Assert.Equal(first, Assert.Single(stored).Value);
            Assert.Equal(first, stored["admins"]);
        }

        [Fact]
        public async Task LoadReturnsSnapshot()
        {
            var store = new MemoryGroupRoleMapStore();
            var before = await store.LoadAsync(default);
            await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid() }, default);
            Assert.Empty(before);
        }
    }

    public class JsonStoreTests : IDisposable
    {
        readonly string Path = Make.TempPath(".json");

        public void Dispose()
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void RequiresPath(string? path) =>
            Assert.Throws<ArgumentException>(() => new JsonFileGroupRoleMapStore(path!));

        [Fact]
        public async Task MissingFileLoadsEmpty() =>
            Assert.Empty(await new JsonFileGroupRoleMapStore(Path).LoadAsync(default));

        [Fact]
        public async Task SaveCreatesDirectoryAndFile()
        {
            var nested = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Path)!,
                Guid.NewGuid().ToString("N"),
                "map.json"
            );
            try
            {
                var id = Guid.NewGuid();
                await new JsonFileGroupRoleMapStore(nested).SaveAsync(
                    new Dictionary<string, Guid> { ["Admins"] = id },
                    default
                );
                var json = JsonSerializer.Deserialize<Dictionary<string, Guid>>(File.ReadAllText(nested));
                Assert.Equal(id, json!["Admins"]);
                Assert.False(File.Exists(nested + ".tmp"));
            }
            finally
            {
                Directory.Delete(System.IO.Path.GetDirectoryName(nested)!, recursive: true);
            }
        }

        [Fact]
        public async Task SurvivesNewInstanceAndFirstWriterWins()
        {
            var first = Guid.NewGuid();
            await new JsonFileGroupRoleMapStore(Path).SaveAsync(
                new Dictionary<string, Guid> { ["Admins"] = first },
                default
            );
            var store = new JsonFileGroupRoleMapStore(Path);
            await store.SaveAsync(
                new Dictionary<string, Guid> { ["admins"] = Guid.NewGuid(), ["Editors"] = Guid.NewGuid() },
                default
            );

            var stored = await store.LoadAsync(default);
            Assert.Equal(2, stored.Count);
            Assert.Equal(first, stored["ADMINS"]);
        }

        [Fact]
        public async Task SaveWithNothingNewLeavesFileAlone()
        {
            var store = new JsonFileGroupRoleMapStore(Path);
            var entries = new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid() };
            await store.SaveAsync(entries, default);
            var written = File.GetLastWriteTimeUtc(Path);
            File.SetLastWriteTimeUtc(Path, written.AddMinutes(-5));

            await store.SaveAsync(entries, default);
            Assert.Equal(written.AddMinutes(-5), File.GetLastWriteTimeUtc(Path));
        }

        [Fact]
        public async Task FileIsReadableOnlyByOwner()
        {
            if (OperatingSystem.IsWindows())
                return;
            await new JsonFileGroupRoleMapStore(Path).SaveAsync(
                new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid() },
                default
            );
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path));
        }

        [Fact]
        public async Task ExistingLooseFileIsTightenedOnSave()
        {
            if (OperatingSystem.IsWindows())
                return;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, "{}");
            File.SetUnixFileMode(Path, (UnixFileMode)0b110_110_110);
            File.WriteAllText(Path + ".tmp", "stale");
            File.SetUnixFileMode(Path + ".tmp", (UnixFileMode)0b110_110_110);

            await new JsonFileGroupRoleMapStore(Path).SaveAsync(
                new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid() },
                default
            );
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path));
        }

        [Fact]
        public async Task FileListingANameTwiceFails()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(
                Path,
                $"{{\"Admins\":\"{Guid.NewGuid()}\",\"admins\":\"{Guid.NewGuid()}\"}}"
            );
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                new JsonFileGroupRoleMapStore(Path).LoadAsync(default)
            );
            Assert.Contains("more than once", ex.Message);
        }

        [Fact]
        public async Task MalformedFileFails()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, "{ not json");
            await Assert.ThrowsAnyAsync<JsonException>(() =>
                new JsonFileGroupRoleMapStore(Path).LoadAsync(default)
            );
        }

        [Fact]
        public async Task ConcurrentSavesOnOneInstanceKeepEveryName()
        {
            var store = new JsonFileGroupRoleMapStore(Path);
            await Task.WhenAll(
                Enumerable.Range(0, 20)
                    .Select(i =>
                        store.SaveAsync(new Dictionary<string, Guid> { [$"G{i}"] = Guid.NewGuid() }, default)
                    )
            );
            Assert.Equal(20, (await store.LoadAsync(default)).Count);
        }
    }

    /// <summary>
    /// Database store logic on SQLite, which needs no server. Server behavior is covered by
    /// DatabaseIntegrationTests.
    /// </summary>
    public class DbStoreTests : IDisposable
    {
        readonly SqliteConnection KeepAlive;
        readonly System.Data.Common.DbDataSource DataSource;

        public DbStoreTests()
        {
            var cs = $"Data Source=grouprolemap-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            KeepAlive = new SqliteConnection(cs);
            KeepAlive.Open();
            DataSource = SqliteFactory.Instance.CreateDataSource(cs);
        }

        public void Dispose()
        {
            DataSource.Dispose();
            KeepAlive.Dispose();
        }

        const string CreateSql =
            "CREATE TABLE IF NOT EXISTS GroupRoleMap (GroupName TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, ObjectId TEXT NOT NULL)";

        DbGroupRoleMapStore NewStore(string? create = CreateSql) => new(DataSource, "GroupRoleMap", create);

        [Fact]
        public void RequiresDataSource() =>
            Assert.Throws<ArgumentNullException>(() => new DbGroupRoleMapStore(null!));

        [Theory]
        [InlineData("x; DROP TABLE y")]
        [InlineData("GroupRoleMap--")]
        [InlineData("a.b.c")]
        [InlineData("1table")]
        [InlineData("[GroupRoleMap]")]
        [InlineData("\"GroupRoleMap\"")]
        [InlineData("")]
        public void RejectsUnsafeTableName(string table)
        {
            Assert.Throws<ArgumentException>(() => new DbGroupRoleMapStore(DataSource, table));
            Assert.Throws<ArgumentException>(() => DbGroupRoleMapStore.SqlServer(DataSource, table));
            Assert.Throws<ArgumentException>(() => DbGroupRoleMapStore.PgSql(DataSource, table));
        }

        [Theory]
        [InlineData("GroupRoleMap")]
        [InlineData("dbo.GroupRoleMap")]
        [InlineData("app_schema.group_role_map_2")]
        public void AcceptsPlainOrSchemaQualifiedName(string table) =>
            Assert.NotNull(new DbGroupRoleMapStore(DataSource, table));

        [Fact]
        public async Task CreatesTableOnFirstUse()
        {
            var store = NewStore();
            Assert.Empty(await store.LoadAsync(default));
        }

        [Fact]
        public async Task WithoutCreateMissingTableFails() =>
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() =>
                NewStore(create: null).LoadAsync(default)
            );

        [Fact]
        public async Task FirstWriterWinsAcrossInstances()
        {
            var first = Guid.NewGuid();
            await NewStore().SaveAsync(new Dictionary<string, Guid> { ["Admins"] = first }, default);
            await NewStore().SaveAsync(
                new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid(), ["Editors"] = Guid.NewGuid() },
                default
            );

            var stored = await NewStore().LoadAsync(default);
            Assert.Equal(2, stored.Count);
            Assert.Equal(first, stored["admins"]);
        }

        [Fact]
        public async Task InvalidStoredObjectIdFails()
        {
            await NewStore().LoadAsync(default);
            await using (var insert = DataSource.CreateCommand(
                "INSERT INTO GroupRoleMap (GroupName, ObjectId) VALUES ('Admins', 'nope')"
            ))
                await insert.ExecuteNonQueryAsync();

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() => NewStore().LoadAsync(default));
            Assert.Contains("invalid Object ID", ex.Message);
        }

        [Fact]
        public async Task DoesNotDisposeBorrowedDataSource()
        {
            var store = NewStore();
            await store.LoadAsync(default);
            await store.DisposeAsync();

            await using var command = DataSource.CreateCommand("SELECT COUNT(*) FROM GroupRoleMap");
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
    }
}
