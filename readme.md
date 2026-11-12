# AzureAD Groups ClaimsTransform

## Purpose

Extension to the Microsoft.Identity.Web package that turns the security group Object IDs Entra ID puts in the token `groups` claim into Role claims. Groups are matched by Object ID against a map built once at startup, so a second group with the same display name never grants a role. Entra ID is not queried during sign in.

## Configuration

### `Startup.cs`

```csharp
  public void ConfigureServices(IServiceCollection services)
  {
      services.Configure<CookiePolicyOptions>(options =>
      {
          // This lambda determines whether user consent for non-essential cookies is needed for a given request.
          options.CheckConsentNeeded = context => true;
          options.MinimumSameSitePolicy = SameSiteMode.Unspecified;
          // Handling SameSite cookie according to https://docs.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-3.1
          options.HandleSameSiteCookieCompatibility();
      });

      // Sign-in users with the Microsoft identity platform
      services.AddMicrosoftIdentityWebAppAuthentication(Configuration)
          .EnableTokenAcquisitionToCallDownstreamApi(new string[] { "User.Read" })
          .AddInMemoryTokenCaches();

      services.AddControllersWithViews().AddMicrosoftIdentityUI();
      services.AddRazorPages();

      // expected group names, looked up once at startup
      services.AddWolfeReiterAzureGroupsClaimsTransform(
          Configuration,
          new[] { "Editors", "Auditors" });
  }
```

### Groups claim in the token

In the app registration under Token configuration, add a groups claim of type Security groups for the ID token. Choosing "Groups assigned to the application" keeps the claim small. A user in more groups than fit in the token gets no `groups` claim, and the transform then adds no roles and adds a `transform-error` claim explaining why.

### Group to role map

Only groups in the map produce role claims. The map comes from three sources, and at least one must be used or the app does not start.

```csharp
services.AddWolfeReiterAzureGroupsClaimsTransform(Configuration, new[] { "Editors", "Auditors" });
```

```json
"WolfeReiterIdentityClaims": {
  "GroupRoles": {
    "6f1c2a4e-0b7d-4c55-9f0e-3d2a1b9c8e77": "Administrators"
  },
  "GroupNames": [ "Reviewers" ]
}
```

`GroupRoles` maps a group Object ID to the role claim value. Nothing is looked up, and when every role comes from `GroupRoles` no store is created, so `GroupRoleMapStore` and its settings are ignored.

Group names passed to `AddWolfeReiterAzureGroupsClaimsTransform` and those in `GroupNames` are combined and resolved to Object IDs once, when the app starts. The role claim value is the name as written. Each name must match exactly one security group. No match, or more than one, is a critical error. It is logged at Critical level and `GroupRoleMapException` is thrown unhandled, so the app does not start. If the map is somehow first built during a request, the same exception is logged and thrown from the claims transformation. Microsoft 365 groups are not considered. A name that is already a role value in `GroupRoles` is not looked up. Startup lookup needs the `Group.Read.All` or `Directory.Read.All` application permission and the `TenantId`, `ClientId` and `ClientSecret` from the `AzureAD` section.

Resolved Object IDs are permanent. With nothing else configured they are kept in memory, so the lookup runs once per launch. A persistent store lets later launches reuse them without searching, and a duplicate group created after the first launch then has no effect. `GroupRoleMapStore` picks where they are kept.

| `GroupRoleMapStore` | Kept in | Settings |
| --- | --- | --- |
| `Memory` | process memory, looked up once per launch | none |
| `Json` | a JSON file, for a single instance | `GroupRoleMapJsonPath` |
| `SqlServer` | a SQL Server table | `GroupRoleMapConnectionString`, or the app `DbDataSource` |
| `PgSql` | a PostgreSQL table | `GroupRoleMapConnectionString`, or the app `DbDataSource` |

When `GroupRoleMapStore` is not set, `Json` is used if `GroupRoleMapJsonPath` is set and `Memory` otherwise.

```json
"WolfeReiterIdentityClaims": {
  "GroupNames": [ "Editors", "Auditors" ],
  "GroupRoleMapStore": "PgSql",
  "GroupRoleMapConnectionString": "Host=db;Database=app;Username=app;Password=...",
  "GroupRoleMapTable": "GroupRoleMap",
  "GroupRoleMapCreateTable": true
}
```

When `GroupRoleMapConnectionString` is empty, the SqlServer and PgSql stores use the `DbDataSource` registered in the service collection and sign in the same way as the rest of the app. This covers a data source that signs in to Aurora or RDS with IAM auth tokens. The store does not dispose that data source.

```csharp
var dataSource = PgSqlDataSource.Create(connectionString, "AwsIam"); // an NpgsqlDataSource
services.AddSingleton<DbDataSource>(dataSource);
services.AddWolfeReiterAzureGroupsClaimsTransform(Configuration, new[] { "Editors" });
```

```json
"WolfeReiterIdentityClaims": { "GroupRoleMapStore": "PgSql" }
```

The SqlServer and PgSql stores create the table on first use unless `GroupRoleMapCreateTable` is false, in which case create it ahead of time. `GroupRoleMapTable` may be schema qualified, such as `dbo.GroupRoleMap`. Several instances may share the table, and the first one to store a name wins.

```sql
CREATE TABLE GroupRoleMap (
    GroupName varchar(256) NOT NULL PRIMARY KEY,
    ObjectId  char(36)     NOT NULL
);
```

Any other ADO.NET provider that accepts `@` named parameters can be used by registering its data source in code. The table is then not created automatically.

```csharp
services.AddWolfeReiterAzureGroupsClaimsTransform(Configuration);
services.AddWolfeReiterGroupRoleMapDbStore(dataSource);
```

To have a stored name looked up again, delete its row or JSON entry and restart. A custom store can be registered by implementing `IGroupRoleMapStore`.



### Upgrading

Role claims are no longer cached per user and Entra ID is no longer queried at sign in. These members are marked obsolete as compile errors and throw `NotSupportedException` if reached by reflection.

* The `IDistributedCache` extensions `SetGroupClaimsAsync`, `GetGroupClaimsAsync` and `RemoveGroupClaimsAsync`, and `AsyncResult<T>`. Remove the `RemoveGroupClaimsAsync` call at sign out. `AddDistributedMemoryCache` is not needed for this library.
* `GroupClaimsCacheAbsoluteExpirationHours`, `GroupClaimsCacheSlidingExpirationMinutes` and `DistributedCacheEntryOptions` on `GraphUtilityServiceOptions`. Leftover keys in app settings are ignored.
* `IGraphUtilityService.GroupsAsync`. Custom implementations can drop it.

## Tests

`dotnet test` runs everything that needs no database server. The SqlServer and PgSql integration tests are skipped unless `GROUPROLEMAP_TEST_SQLSERVER` or `GROUPROLEMAP_TEST_PGSQL` holds a connection string to a test server. `tests/run-database-tests.sh` starts SQL Server 2022 and PostgreSQL 17 in Docker, sets both variables, runs the whole suite, and removes the containers. Arguments are passed to `dotnet test`.

The PgSql integration tests run three ways, with a static connection string, with the app's registered `DbDataSource`, and with a data source whose password comes from a periodic provider the way an RDS IAM auth token does.
