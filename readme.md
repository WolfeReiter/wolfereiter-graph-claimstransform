# AzureAD Groups ClaimsTransform

## Purpose

Extension to Microsoft.Identity.Web package uses Microsoft Graph API to add AzureAD Group memberships as Role claims.

## Configuration

### `Startup.cs`

```csharp
  public void ConfigureServices(IServiceCollection services)
  {
      ...

      //DistributedMemoryCache implements IDistributedCache with local memory for development 
      //but not suitable for production. Use SQL Server or Redis cache
      services.AddDistributedMemoryCache();
      //service must be scoped -- it runs per user/request context
      services.AddScoped<IClaimsTransformation, AzureGroupClaimsTransform>();

      ...
  }
```

### Account/Logout

TODO: Call the extension method to remove the User (ClaimsPrincipal) values from the cache.