# AzureAD Groups ClaimsTransform

## Purpose

Extension to the Microsoft.Identity.Web package that turns the security group Object IDs Entra ID puts in the token `groups` claim into Role claims. Groups are matched by Object ID against a map built once at startup, so a second group with the same display name never grants a role. Entra ID is not queried during sign in.

## Quick start

### 1. Set up the app registration

* Under Token configuration, add a groups claim of type Security groups for the ID token. "Groups assigned to the application" keeps the claim small.
* Under API permissions, add the Microsoft Graph application permission `GroupMember.Read.All` and grant admin consent. It is used only at startup to look up group names and check stored Object IDs.
* Under Certificates & secrets, create a client secret and put it in `AzureAD:ClientSecret` through user secrets, an environment variable, or a secret manager.
* Set `AzureAD:TenantId` to the tenant GUID. Tokens from any other tenant then get no roles.

### 2. Register the transform

```csharp
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration);

// the role names, each the display name of exactly one security group
builder.Services.AddWolfeReiterAzureGroupsClaimsTransform(
    builder.Configuration,
    new[] { "Editors", "Auditors" });

builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
```

Each name is resolved to a group Object ID at startup and the role is granted by Object ID from then on. Use `[Authorize(Roles = "Editors")]` or `User.IsInRole("Editors")` as usual. Group names can also be listed in `WolfeReiterIdentityClaims:GroupNames`, and groups can be pinned by Object ID in `WolfeReiterIdentityClaims:GroupRoles`, which needs no lookup at all.

### 3. Choose where resolved Object IDs are kept

Without a store setting they are kept in memory and looked up again on every start. In production keep them in a database so a group created later with the same name has no effect. Every option below creates the `GroupRoleMap` table on first use.

#### SQL Server or PostgreSQL on the app's default connection

When the app already registers a `DbDataSource`, the store uses it and signs in exactly as the rest of the app does. Name the store and leave out the connection string.

```json
"WolfeReiterIdentityClaims": { "GroupRoleMapStore": "SqlServer" }
```

```csharp
// SQL Server
builder.Services.AddSingleton<DbDataSource>(
    SqlClientFactory.Instance.CreateDataSource(builder.Configuration.GetConnectionString("Default")!));
```

```json
"WolfeReiterIdentityClaims": { "GroupRoleMapStore": "PgSql" }
```

```csharp
// PostgreSQL
builder.Services.AddSingleton<DbDataSource>(
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Default")!));
```

An app using Entity Framework Core can build the data source once and pass the same instance to `UseSqlServer` or `UseNpgsql` and to `AddSingleton<DbDataSource>`.

#### PostgreSQL on Aurora or RDS with IAM authentication

Register an `NpgsqlDataSource` whose password is a fresh IAM auth token, created from the default AWS credentials, which on ECS or EKS are the task or pod role. Tokens are valid for 15 minutes and are checked only when a connection opens, so a 10 minute refresh is enough. This needs the `AWSSDK.RDS` package.

```csharp
using Amazon.RDS.Util;
using Npgsql;

// Host=...;Port=5432;Database=app;Username=app_iam;SSL Mode=VerifyFull
// no password, and SSL is required for IAM sign-in
var pg = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("Default"));
pg.UsePeriodicPasswordProvider(
    async (settings, _) => await RDSAuthTokenGenerator.GenerateAuthTokenAsync(
        settings.Host, settings.Port, settings.Username),
    successRefreshInterval: TimeSpan.FromMinutes(10),
    failureRefreshInterval: TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<DbDataSource>(pg.Build());
```

```json
"WolfeReiterIdentityClaims": { "GroupRoleMapStore": "PgSql" }
```

On the database, create the login with `CREATE USER app_iam; GRANT rds_iam TO app_iam;` and grant it SELECT and INSERT on `GroupRoleMap`, plus CREATE on the schema if the library is to create the table. The role needs an IAM policy allowing `rds-db:connect` on `arn:aws:rds-db:{region}:{account}:dbuser:{db-resource-id}/app_iam`.

#### Azure SQL with Microsoft Entra authentication

Add the `Microsoft.Data.SqlClient.Extensions.Azure` package, which Microsoft.Data.SqlClient 7 needs for Entra sign-in, and register the data source as above with a connection string such as `Server=tcp:{server}.database.windows.net;Database=app;Authentication=Active Directory Default;Encrypt=True`. On Azure this signs in with the app's managed identity. RDS for SQL Server does not support IAM database authentication, so there use a SQL login whose password comes from a secret manager.

#### A connection string just for the store

Either database can also get its own connection string, in which case the library creates and owns the data source.

```json
"WolfeReiterIdentityClaims": {
  "GroupRoleMapStore": "PgSql",
  "GroupRoleMapConnectionString": "Host=db;Database=app;Username=app;Password=..."
}
```

### Migrating an existing project

Earlier versions looked up each user's groups in Graph at every sign in, cached the display names per user, and turned every security group into a role. Roles now come from group Object IDs in the token, matched against a fixed map.

1. In the app registration, add the groups claim for the ID token as in step 1. Without it users get no roles.
2. Grant `GroupMember.Read.All` as an application permission. `Directory.Read.All` as a delegated scope is no longer needed by this library.
3. List every role the app authorizes on, either as names passed to `AddWolfeReiterAzureGroupsClaimsTransform` or in `GroupNames`, or by Object ID in `GroupRoles`. Only these groups grant roles now. The app does not start when none is configured.
4. Choose a store as in step 3.
5. Remove calls to `IDistributedCache.RemoveGroupClaimsAsync`, `GetGroupClaimsAsync` and `SetGroupClaimsAsync`, and any use of `GroupClaimsCacheAbsoluteExpirationHours`, `GroupClaimsCacheSlidingExpirationMinutes`, `DistributedCacheEntryOptions` or `IGraphUtilityService.GroupsAsync`. They are obsolete as compile errors. A custom sign out action that only existed to clear that cache can go, along with `AddDistributedMemoryCache` and `EnableTokenAcquisitionToCallDownstreamApi` if nothing else uses them.
6. A custom `IGraphUtilityService` must implement `SecurityGroupsByDisplayNameAsync` and `GroupsByIdAsync`, or derive from `GraphUtilityService`.
7. Check the first start's log. Each group found by name is logged at Warning with its Object ID so the mapping can be confirmed.

These behaviors differ from earlier versions.

* A group name with no match or more than one match is a critical error and the app does not start.
* Role claims are issued by `WolfeReiter.Identity.Claims` rather than `AzureAD`.
* Groups with dynamic membership never grant a role and are logged at Warning.
* A stored Object ID whose group was renamed, deleted, or turned into something other than a security group stops startup until it is corrected.
* A user in more groups than fit in the token gets no roles and a `transform-error` claim.

## Configuration

### Groups claim in the token

In the app registration under Token configuration, add a groups claim of type Security groups for the ID token. Choosing "Groups assigned to the application" keeps the claim small. A user in more groups than fit in the token gets no `groups` claim, and the transform then adds no roles and adds a `transform-error` claim explaining why.

Only the identity that carries the `oid` claim is read, and only `groups` claims with the same issuer as that `oid` claim count. Others are ignored and logged at Warning. When `TenantId` in the `AzureAD` section is a GUID, the `oid` claim must also come from that tenant, through either `https://login.microsoftonline.com/{tenant}/v2.0` or `https://sts.windows.net/{tenant}/`, or the user gets no roles. Roles are added to that same identity with the issuer `WolfeReiter.Identity.Claims`. The transform handles each user once per request and skips an identity it has already marked.

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

Group names passed to `AddWolfeReiterAzureGroupsClaimsTransform` and those in `GroupNames` are combined and resolved to Object IDs once, when the app starts. The role claim value is the name as written. Each name must match exactly one security group. No match, or more than one, is a critical error. It is logged at Critical level and `GroupRoleMapException` is thrown unhandled, so the app does not start. If the map is somehow first built during a request, the same exception is logged and thrown from the claims transformation. Microsoft 365 groups are not considered. A name that is already a role value in `GroupRoles` is not looked up. The startup lookup signs in to Microsoft Graph as the application with the `TenantId`, `ClientId` and `ClientSecret` from the `AzureAD` section, and needs an application permission to list groups. See [Graph permission and credential](#graph-permission-and-credential).

Each newly resolved name is logged at Warning with the group's Object ID, creation date, and whether it is role-assignable, so the first mapping of a name can be audited.

Resolved Object IDs are permanent. With nothing else configured they are kept in memory, so the lookup runs once per launch, and a startup warning is logged when that happens in the Production environment. A persistent store lets later launches reuse them without searching, and a duplicate group created after the first launch then has no effect. `GroupRoleMapStore` picks where they are kept.

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

When `GroupRoleMapConnectionString` is empty, the SqlServer and PgSql stores use the `DbDataSource` registered in the service collection and sign in the same way as the rest of the app, including with IAM auth tokens or Microsoft Entra authentication. The store does not dispose that data source. See [Choose where resolved Object IDs are kept](#3-choose-where-resolved-object-ids-are-kept) for examples.

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

### Checking stored Object IDs

The store decides which group grants which role, so anyone able to write it could map a role to a group they control. At startup every Object ID read from the store is checked against Microsoft Graph in one request per 15 IDs. The group must still exist, must be a security group, and must have the stored name, ignoring case. When `GroupRoleMapRequireRoleAssignable` is set it must also be role-assignable. Any mismatch is a critical `GroupRoleMapException` and the app does not start. A stored group that now has dynamic membership is the exception, and is ignored with a warning as described under [Dynamic membership groups](#dynamic-membership-groups). Renaming a mapped group therefore stops startup until the name in the configuration and the store is corrected.

This check is on by default. Setting `GroupRoleMapVerifyStored` to false skips it and the Graph call, so stored IDs are trusted as they are.

The check catches a row pointing at a group with a different name, but not one pointing at another group that was given the same name. Requiring role-assignable groups closes that gap, because ordinary users cannot create them.

Limit who can change the store.

* The JSON file is written readable and writable by its owner only on Linux and macOS.
* Give the app's database login SELECT and INSERT on the table and nothing more. It needs CREATE only if the library creates the table, so create the table ahead of time with `GroupRoleMapCreateTable` set to false when the login can be narrowed further.

### Role-assignable groups

Display names are not unique and, by default, any member user can create a security group. If a configured name does not exist yet, has been deleted, or is misspelled, whoever creates a group with that name first is mapped to the role.

Setting `GroupRoleMapRequireRoleAssignable` to true requires every group resolved from a name, and every stored group checked at startup, to be role-assignable (`isAssignableToRole`). Only a Privileged Role Administrator or Global Administrator can create a role-assignable group, so an ordinary user cannot claim the name. A group that is not role-assignable is a critical `GroupRoleMapException`. The setting does not apply to `GroupRoles`, which names Object IDs directly.

```json
"WolfeReiterIdentityClaims": {
  "GroupNames": [ "Administrators" ],
  "GroupRoleMapStore": "PgSql",
  "GroupRoleMapRequireRoleAssignable": true
}
```

It is off by default. Turn it on when the mapped groups can meet these conditions.

* Role-assignable groups need Microsoft Entra ID P1 or P2.
* A group is made role-assignable only when it is created. An existing group cannot be converted, so create a new group and move the members.
* Role-assignable groups cannot have dynamic membership, so none of them is ignored for the reason described under [Dynamic membership groups](#dynamic-membership-groups).
* A tenant can have at most 500 role-assignable groups.
* Only Privileged Role Administrators and Global Administrators can change their membership, unless owners are assigned.

Even with the setting off, the other protections narrow the window. Pinning the Object ID with `GroupRoles`, or creating the groups before the app first starts and using a persistent store, means the name is resolved only once while the real group already exists.

### Graph permission and credential

Grant the application permission `GroupMember.Read.All` with admin consent. It is the least privileged permission that can list groups. `Group.Read.All` and `Directory.Read.All` also work but allow much more, so leave them off unless the app needs them for something else.

Graph is called only at startup, for group names that are not already in the store and, unless `GroupRoleMapVerifyStored` is false, to check the Object IDs that are. Signing in to Entra ID and building role claims at sign in never call Graph. When every role comes from `GroupRoles`, this library makes no Graph call at all and needs neither the permission nor the client secret.

The lookup authenticates with the client secret. Keep it out of `appsettings.json`, for example in user secrets during development and in a secret manager or environment variable in production, and rotate it before it expires. A secret that has expired fails the lookup, which stops startup whenever group names are configured.

### Dynamic membership groups

A group with dynamic membership takes its members from a rule over user attributes, so anyone able to change the attributes the rule reads could give themselves the role. Such groups never grant a role. A group whose `groupTypes` includes `DynamicMembership` is treated like a Microsoft 365 group and is ignored, and each one found is logged at Warning.

* When a name is resolved, dynamic groups are left out before matching. A dynamic group with the same name as the intended group is therefore not a duplicate. If every match is dynamic, the name has no match, which is a critical `GroupRoleMapException`.
* When stored Object IDs are checked at startup, a stored group that now has dynamic membership is left out of the map and logged at Warning, and the app still starts. Its role is granted to no one until the group is changed back to assigned membership. The store entry is kept, so the name is not looked up again.
* `GroupRoles` is not checked, since it names Object IDs directly and makes no Graph call, and stored IDs are not checked when `GroupRoleMapVerifyStored` is false.

To use an ignored group, change it to assigned membership. In the Microsoft Entra admin center open the group, then Properties, and set Membership type to Assigned. The current members stay. With Microsoft Graph, remove `DynamicMembership` from `groupTypes`.

```
PATCH https://graph.microsoft.com/v1.0/groups/{id}
{ "groupTypes": [] }
```

## Tests

`dotnet test` runs everything that needs no database server. The SqlServer and PgSql integration tests are skipped unless `GROUPROLEMAP_TEST_SQLSERVER` or `GROUPROLEMAP_TEST_PGSQL` holds a connection string to a test server. `tests/run-database-tests.sh` starts SQL Server 2022 and PostgreSQL 17 in Docker, sets both variables, runs the whole suite, and removes the containers. Arguments are passed to `dotnet test`.

The PgSql integration tests run three ways, with a static connection string, with the app's registered `DbDataSource`, and with a data source whose password comes from a periodic provider the way an RDS IAM auth token does.
