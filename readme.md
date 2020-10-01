# AzureAD Groups ClaimsTransform

## Purpose

Extension to Microsoft.Identity.Web package uses Microsoft Graph API to add AzureAD Group memberships as Role claims.

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

      //in production use DistributedSqlServerCache or Redis Cache
      services.AddDistributedMemoryCache();
      /*
      services.AddStackExchangeRedisCache(options =>
      {
          options.Configuration = "host:4445";
      });
      */
      // Sign-in users with the Microsoft identity platform
      services.AddMicrosoftIdentityWebAppAuthentication(Configuration)
          .EnableTokenAcquisitionToCallDownstreamApi(new string[] { "User.Read", "Directory.Read.All" })
          .AddDistributedTokenCaches();

      services.AddControllersWithViews().AddMicrosoftIdentityUI();
      services.AddRazorPages();

      services.AddWolfeReiterAzureGroupsClaimsTransform();
  }
```

### Account/Logout

TODO: Call the extension method to remove the User (ClaimsPrincipal) values from the cache.