using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;

var builder = WebApplication.CreateBuilder(args);

// Sign in with Microsoft Entra ID. Roles come from the groups claim in the ID token, so no
// downstream API, token cache or distributed cache is needed.
builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration);

// Group names are read from WolfeReiterIdentityClaims:GroupNames. They can also be passed here,
// for example AddWolfeReiterAzureGroupsClaimsTransform(builder.Configuration, new[] { "Editors" }).
builder.Services.AddWolfeReiterAzureGroupsClaimsTransform(builder.Configuration);

builder.Services.AddControllersWithViews().AddMicrosoftIdentityUI();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStatusCodePages();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
