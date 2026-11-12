using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Web;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    public class TransformTests
    {
        readonly FakeGraph Graph = new();

        AzureGroupsClaimsTransform NewTransform(Dictionary<string, string>? roles = null, params string[] names) =>
            new(Make.Map(Graph, null, roles, names), NullLoggerFactory.Instance);

        static string[] Roles(ClaimsPrincipal principal) =>
            principal.FindAll(ClaimTypes.Role).Select(x => x.Value).OrderBy(x => x).ToArray();

        static Claim Group(string id) => new("groups", id);

        [Fact]
        public async Task MapsKnownGroupsAndIgnoresOthers()
        {
            var admins = Graph.Add("Admins");
            var principal = Make.Principal(
                Group(admins.Id!),
                Group(Guid.NewGuid().ToString()),
                Group("not-a-guid")
            );

            var result = await NewTransform(null, "Admins").TransformAsync(principal);

            Assert.Equal(new[] { "Admins" }, Roles(result));
            Assert.Empty(result.FindAll("transform-error"));
        }

        [Fact]
        public async Task GroupWithSameNameButOtherIdGetsNoRole()
        {
            Graph.Add("Admins");
            var transform = NewTransform(null, "Admins");
            var impostor = FakeGraph.Group("Admins");

            var result = await transform.TransformAsync(Make.Principal(Group(impostor.Id!)));
            Assert.Empty(Roles(result));
        }

        [Fact]
        public async Task GroupIdMatchesInAnyCase()
        {
            var id = Guid.NewGuid();
            var result = await NewTransform(new() { [id.ToString()] = "Admins" })
                .TransformAsync(Make.Principal(Group(id.ToString().ToUpperInvariant())));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Fact]
        public async Task StaticAndDiscoveredRolesCombine()
        {
            var editors = Graph.Add("Editors");
            var id = Guid.NewGuid();
            var result = await NewTransform(new() { [id.ToString()] = "Admins" }, "Editors")
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
            var result = await NewTransform(new() { [id.ToString()] = "Admins" })
                .TransformAsync(Make.Principal(Group(id.ToString()), new Claim(ClaimTypes.Role, "Admins")));
            Assert.Equal(new[] { "Admins" }, Roles(result));
        }

        [Fact]
        public async Task RoleClaimHasAzureAdIssuer()
        {
            var id = Guid.NewGuid();
            var result = await NewTransform(new() { [id.ToString()] = "Admins" })
                .TransformAsync(Make.Principal(Group(id.ToString())));
            var role = result.FindFirst(ClaimTypes.Role)!;
            Assert.Equal("AzureAD", role.Issuer);
            Assert.True(result.IsInRole("Admins"));
        }

        [Fact]
        public async Task MarksPrincipalWithTransformClaim()
        {
            var result = await NewTransform(new() { [Guid.NewGuid().ToString()] = "A" })
                .TransformAsync(Make.Principal());
            Assert.Equal(typeof(AzureGroupsClaimsTransform).FullName, result.FindFirst("transform")!.Value);
        }

        [Fact]
        public async Task NoGroupsClaimAddsNoRoles()
        {
            var result = await NewTransform(new() { [Guid.NewGuid().ToString()] = "A" })
                .TransformAsync(Make.Principal());
            Assert.Empty(Roles(result));
            Assert.Empty(result.FindAll("transform-error"));
        }

        [Fact]
        public async Task PrincipalWithoutObjectIdIsLeftAlone()
        {
            var id = Guid.NewGuid();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { Group(id.ToString()) }, "test"));

            var result = await NewTransform(new() { [id.ToString()] = "Admins" }).TransformAsync(principal);

            Assert.Empty(Roles(result));
            Assert.Null(result.FindFirst("transform"));
        }

        [Fact]
        public async Task SecondCallInScopeReturnsSamePrincipalUnchanged()
        {
            var id = Guid.NewGuid();
            var transform = NewTransform(new() { [id.ToString()] = "Admins" });
            var principal = Make.Principal(Group(id.ToString()));

            var first = await transform.TransformAsync(principal);
            var second = await transform.TransformAsync(principal);

            Assert.Same(first, second);
            Assert.Single(second.FindAll(ClaimTypes.Role));
            Assert.Single(second.FindAll("transform"));
        }

        [Fact]
        public async Task OverageFromHasGroupsIsFlagged()
        {
            var result = await NewTransform(new() { [Guid.NewGuid().ToString()] = "A" })
                .TransformAsync(Make.Principal(new Claim("hasgroups", "true")));
            Assert.Contains("too many groups", result.FindFirst("transform-error")!.Value);
        }

        [Fact]
        public async Task OverageFromClaimNamesIsFlagged()
        {
            var result = await NewTransform(new() { [Guid.NewGuid().ToString()] = "A" })
                .TransformAsync(Make.Principal(new Claim("_claim_names", "{\"groups\":\"src1\"}")));
            Assert.Empty(Roles(result));
            Assert.Contains("too many groups", result.FindFirst("transform-error")!.Value);
        }

        [Fact]
        public async Task ClaimNamesWithoutGroupsIsNotOverage()
        {
            var result = await NewTransform(new() { [Guid.NewGuid().ToString()] = "A" })
                .TransformAsync(Make.Principal(new Claim("_claim_names", "{\"other\":\"src1\"}")));
            Assert.Null(result.FindFirst("transform-error"));
        }

        [Fact]
        public async Task NameWithNoMatchIsCriticalAndUnhandled()
        {
            var logs = new ListLoggerFactory();
            var transform = new AzureGroupsClaimsTransform(Make.Map(Graph, null, null, "Admins"), logs);

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                transform.TransformAsync(Make.Principal(Group(Guid.NewGuid().ToString())))
            );
            Assert.Contains("No security group is named 'Admins'", ex.Message);
            Assert.Contains(logs.Logger.Entries, x => x.Level == Microsoft.Extensions.Logging.LogLevel.Critical && x.Exception == ex);
        }

        [Fact]
        public async Task NameWithSeveralMatchesIsCriticalAndUnhandled()
        {
            Graph.Add("Admins");
            Graph.Add("Admins");
            var logs = new ListLoggerFactory();
            var transform = new AzureGroupsClaimsTransform(Make.Map(Graph, null, null, "Admins"), logs);

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                transform.TransformAsync(Make.Principal())
            );
            Assert.Contains("2 security groups are named 'Admins'", ex.Message);
            Assert.Contains(logs.Logger.Entries, x => x.Level == Microsoft.Extensions.Logging.LogLevel.Critical && x.Exception == ex);
        }
    }
}
