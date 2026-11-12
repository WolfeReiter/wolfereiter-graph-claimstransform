using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    public class TransformTests
    {
        const string Tenant = "7f3c1d2e-4b5a-4c6d-8e9f-0a1b2c3d4e5f";
        const string V2Issuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
        const string V1Issuer = "https://sts.windows.net/" + Tenant + "/";

        readonly FakeGraph Graph = new();

        AzureGroupsClaimsTransform NewTransform(
            Dictionary<string, string>? roles = null,
            string[]? names = null,
            string? tenantId = null,
            ILoggerFactory? logs = null
        ) =>
            new(
                Make.Map(Graph, null, roles, names ?? Array.Empty<string>(), o => o.TenantId = tenantId),
                Options.Create(new GraphUtilityServiceOptions { TenantId = tenantId }),
                logs ?? NullLoggerFactory.Instance
            );

        static AzureGroupsClaimsTransform StaticTransform(Guid id, string role = "Admins", string? tenantId = null) =>
            new(
                Make.Map(new FakeGraph(), null, new() { [id.ToString()] = role }),
                Options.Create(new GraphUtilityServiceOptions { TenantId = tenantId }),
                NullLoggerFactory.Instance
            );

        static string[] Roles(ClaimsPrincipal principal) =>
            principal.FindAll(ClaimTypes.Role).Select(x => x.Value).OrderBy(x => x).ToArray();

        static Claim Group(string id, string? issuer = null) =>
            issuer == null ? new("groups", id) : new("groups", id, ClaimValueTypes.String, issuer);

        static ClaimsIdentity EntraIdentity(string issuer, params string[] groups) =>
            new(
                groups
                    .Select(x => Group(x, issuer))
                    .Append(new Claim(ClaimConstants.Oid, Guid.NewGuid().ToString(), ClaimValueTypes.String, issuer)),
                "AuthenticationTypes.Federation"
            );

        // ---- mapping

        [Fact]
        public async Task MapsKnownGroupsAndIgnoresOthers()
        {
            var admins = Graph.Add("Admins");
            var principal = Make.Principal(
                Group(admins.Id!),
                Group(Guid.NewGuid().ToString()),
                Group("not-a-guid")
            );

            var result = await NewTransform(names: new[] { "Admins" }).TransformAsync(principal);

            Assert.Equal(new[] { "Admins" }, Roles(result));
            Assert.Empty(result.FindAll("transform-error"));
        }

        [Fact]
        public async Task GroupWithSameNameButOtherIdGetsNoRole()
        {
            Graph.Add("Admins");
            var impostor = FakeGraph.Group("Admins");
            var result = await NewTransform(names: new[] { "Admins" })
                .TransformAsync(Make.Principal(Group(impostor.Id!)));
            Assert.Empty(Roles(result));
        }

        [Fact]
        public async Task GroupIdMatchesInAnyCase()
        {
            var id = Guid.NewGuid();
            var result = await StaticTransform(id)
                .TransformAsync(Make.Principal(Group(id.ToString().ToUpperInvariant())));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Fact]
        public async Task StaticAndDiscoveredRolesCombine()
        {
            var editors = Graph.Add("Editors");
            var id = Guid.NewGuid();
            var result = await NewTransform(new() { [id.ToString()] = "Admins" }, new[] { "Editors" })
                .TransformAsync(Make.Principal(Group(id.ToString()), Group(editors.Id!)));
            Assert.Equal(new[] { "Admins", "Editors" }, Roles(result));
        }

        [Fact]
        public async Task TwoGroupsWithOneRoleAddItOnce()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var result = await NewTransform(new() { [a.ToString()] = "Admins", [b.ToString()] = "Admins" })
                .TransformAsync(Make.Principal(Group(a.ToString()), Group(b.ToString())));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Fact]
        public async Task ExistingRoleClaimIsNotDuplicated()
        {
            var id = Guid.NewGuid();
            var result = await StaticTransform(id)
                .TransformAsync(Make.Principal(Group(id.ToString()), new Claim(ClaimTypes.Role, "Admins")));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Fact]
        public async Task RoleClaimHasTransformIssuer()
        {
            var id = Guid.NewGuid();
            var result = await StaticTransform(id).TransformAsync(Make.Principal(Group(id.ToString())));
            Assert.Equal(AzureGroupsClaimsTransform.TransformIssuer, result.FindFirst(ClaimTypes.Role)!.Issuer);
            Assert.True(result.IsInRole("Admins"));
        }

        [Fact]
        public async Task MarksPrincipalWithTransformClaim()
        {
            var result = await StaticTransform(Guid.NewGuid()).TransformAsync(Make.Principal());
            var marker = result.FindFirst("transform")!;
            Assert.Equal(typeof(AzureGroupsClaimsTransform).FullName, marker.Value);
            Assert.Equal(AzureGroupsClaimsTransform.TransformIssuer, marker.Issuer);
        }

        [Fact]
        public async Task NoGroupsClaimAddsNoRoles()
        {
            var result = await StaticTransform(Guid.NewGuid()).TransformAsync(Make.Principal());
            Assert.Empty(Roles(result));
            Assert.Empty(result.FindAll("transform-error"));
        }

        [Fact]
        public async Task PrincipalWithoutObjectIdIsLeftAlone()
        {
            var id = Guid.NewGuid();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { Group(id.ToString()) }, "test"));

            var result = await StaticTransform(id).TransformAsync(principal);

            Assert.Empty(Roles(result));
            Assert.Null(result.FindFirst("transform"));
        }

        [Fact]
        public async Task LongObjectIdClaimTypeIsRecognized()
        {
            var id = Guid.NewGuid();
            var principal = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { Group(id.ToString()), new Claim(ClaimConstants.ObjectId, Guid.NewGuid().ToString()) },
                    "test"
                )
            );
            Assert.Equal(new[] { "Admins" }, Roles(await StaticTransform(id).TransformAsync(principal)));
        }

        // ---- only the Entra ID identity and issuer are trusted

        [Fact]
        public async Task GroupsOnAnotherIdentityAreIgnored()
        {
            var id = Guid.NewGuid();
            var principal = new ClaimsPrincipal(
                new[]
                {
                    new ClaimsIdentity(new[] { Group(id.ToString()) }, "local"),
                    EntraIdentity(V2Issuer),
                }
            );

            var result = await StaticTransform(id, tenantId: Tenant).TransformAsync(principal);
            Assert.Empty(Roles(result));
        }

        [Fact]
        public async Task RolesAreAddedToTheEntraIdentity()
        {
            var id = Guid.NewGuid();
            var local = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "x") }, "local");
            var entra = EntraIdentity(V2Issuer, id.ToString());
            var principal = new ClaimsPrincipal(new[] { local, entra });

            await StaticTransform(id, tenantId: Tenant).TransformAsync(principal);

            Assert.True(entra.HasClaim(ClaimTypes.Role, "Admins"));
            Assert.False(local.HasClaim(x => x.Type == ClaimTypes.Role));
        }

        [Fact]
        public async Task GroupsClaimFromOtherIssuerIsIgnoredAndLogged()
        {
            var id = Guid.NewGuid();
            var other = Guid.NewGuid();
            var identity = EntraIdentity(V2Issuer, id.ToString());
            identity.AddClaim(Group(other.ToString(), "https://evil.example/"));
            var logs = new ListLoggerFactory();
            var transform = new AzureGroupsClaimsTransform(
                Make.Map(new FakeGraph(), null, new() { [id.ToString()] = "Admins", [other.ToString()] = "Owners" }),
                Options.Create(new GraphUtilityServiceOptions { TenantId = Tenant }),
                logs
            );

            var result = await transform.TransformAsync(new ClaimsPrincipal(identity));

            Assert.Equal(new[] { "Admins" }, Roles(result));
            Assert.Contains(logs.Logger.Entries, x => x.Level == LogLevel.Warning && x.Message.Contains("1 groups claims"));
        }

        [Theory]
        [InlineData(V2Issuer)]
        [InlineData(V1Issuer)]
        [InlineData("https://LOGIN.microsoftonline.com/" + Tenant + "/v2.0")]
        public async Task TenantIssuerIsAccepted(string issuer)
        {
            var id = Guid.NewGuid();
            var result = await StaticTransform(id, tenantId: Tenant)
                .TransformAsync(new ClaimsPrincipal(EntraIdentity(issuer, id.ToString())));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Theory]
        [InlineData("https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/v2.0")]
        [InlineData("https://sts.windows.net/00000000-0000-0000-0000-000000000001/")]
        [InlineData("LOCAL AUTHORITY")]
        public async Task OtherTenantGetsNoRoles(string issuer)
        {
            var id = Guid.NewGuid();
            var logs = new ListLoggerFactory();
            var transform = new AzureGroupsClaimsTransform(
                Make.Map(new FakeGraph(), null, new() { [id.ToString()] = "Admins" }),
                Options.Create(new GraphUtilityServiceOptions { TenantId = Tenant }),
                logs
            );

            var result = await transform.TransformAsync(new ClaimsPrincipal(EntraIdentity(issuer, id.ToString())));

            Assert.Empty(Roles(result));
            Assert.Null(result.FindFirst("transform"));
            Assert.Contains(logs.Logger.Entries, x => x.Level == LogLevel.Warning);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("contoso.onmicrosoft.com")]
        [InlineData("organizations")]
        public async Task TenantIssuerIsNotCheckedWhenTenantIdIsNotAGuid(string? tenantId)
        {
            var id = Guid.NewGuid();
            var result = await StaticTransform(id, tenantId: tenantId)
                .TransformAsync(new ClaimsPrincipal(EntraIdentity("https://any.example/", id.ToString())));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        // ---- repeated calls in one scope

        [Fact]
        public async Task SamePrincipalTwiceIsTransformedOnce()
        {
            var id = Guid.NewGuid();
            var transform = StaticTransform(id);
            var principal = Make.Principal(Group(id.ToString()));

            var first = await transform.TransformAsync(principal);
            var second = await transform.TransformAsync(principal);

            Assert.Same(principal, first);
            Assert.Same(principal, second);
            Assert.Single(second.FindAll(ClaimTypes.Role));
            Assert.Single(second.FindAll("transform"));
        }

        [Fact]
        public async Task DifferentPrincipalInSameScopeIsTransformedOnItsOwn()
        {
            var admins = Guid.NewGuid();
            var editors = Guid.NewGuid();
            var transform = new AzureGroupsClaimsTransform(
                Make.Map(new FakeGraph(), null, new() { [admins.ToString()] = "Admins", [editors.ToString()] = "Editors" }),
                Options.Create(new GraphUtilityServiceOptions()),
                NullLoggerFactory.Instance
            );
            var first = Make.Principal(Group(admins.ToString()));
            var second = Make.Principal(Group(editors.ToString()));

            await transform.TransformAsync(first);
            var result = await transform.TransformAsync(second);

            Assert.Same(second, result);
            Assert.Equal(new[] { "Editors" }, Roles(result));
            Assert.Equal(new[] { "Admins" }, Roles(first));
        }

        [Fact]
        public async Task AlreadyTransformedIdentityIsSkippedInANewScope()
        {
            var id = Guid.NewGuid();
            var principal = Make.Principal(Group(id.ToString()));
            await StaticTransform(id).TransformAsync(principal);

            var result = await StaticTransform(id).TransformAsync(principal);
            Assert.Single(result.FindAll(ClaimTypes.Role));
        }

        [Fact]
        public async Task ForgedMarkerClaimDoesNotSkipTransform()
        {
            var id = Guid.NewGuid();
            var principal = Make.Principal(Group(id.ToString()), new Claim("transform", "x"));
            var result = await StaticTransform(id).TransformAsync(principal);
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        // ---- overage

        [Fact]
        public async Task OverageFromHasGroupsIsFlagged()
        {
            var result = await StaticTransform(Guid.NewGuid())
                .TransformAsync(Make.Principal(new Claim("hasgroups", "true")));
            Assert.Contains("too many groups", result.FindFirst("transform-error")!.Value);
        }

        [Fact]
        public async Task OverageFromClaimNamesIsFlagged()
        {
            var result = await StaticTransform(Guid.NewGuid())
                .TransformAsync(Make.Principal(new Claim("_claim_names", "{\"groups\":\"src1\"}")));
            Assert.Empty(Roles(result));
            Assert.Contains("too many groups", result.FindFirst("transform-error")!.Value);
        }

        [Fact]
        public async Task ClaimNamesWithoutGroupsIsNotOverage()
        {
            var result = await StaticTransform(Guid.NewGuid())
                .TransformAsync(Make.Principal(new Claim("_claim_names", "{\"other\":\"src1\"}")));
            Assert.Null(result.FindFirst("transform-error"));
        }

        // ---- map failures are critical and unhandled

        [Fact]
        public async Task NameWithNoMatchIsCriticalAndUnhandled()
        {
            var logs = new ListLoggerFactory();
            var transform = NewTransform(names: new[] { "Admins" }, logs: logs);

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                transform.TransformAsync(Make.Principal(Group(Guid.NewGuid().ToString())))
            );
            Assert.Contains("No security group is named 'Admins'", ex.Message);
            Assert.Contains(logs.Logger.Entries, x => x.Level == LogLevel.Critical && x.Exception == ex);
        }

        [Fact]
        public async Task NameWithSeveralMatchesIsCriticalAndUnhandled()
        {
            Graph.Add("Admins");
            Graph.Add("Admins");
            var logs = new ListLoggerFactory();
            var transform = NewTransform(names: new[] { "Admins" }, logs: logs);

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() => transform.TransformAsync(Make.Principal()));
            Assert.Contains("2 security groups are named 'Admins'", ex.Message);
            Assert.Contains(logs.Logger.Entries, x => x.Level == LogLevel.Critical && x.Exception == ex);
        }
    }
}
