using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    public class GroupRoleMapTests
    {
        [Fact]
        public async Task NothingConfiguredIsAnError()
        {
            var map = Make.Map(new FakeGraph());
            Assert.False(map.IsConfigured);
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() => map.GetAsync());
            Assert.Contains("no group can be mapped", ex.Message);
        }

        [Fact]
        public void StaticRolesAloneAreConfigured() =>
            Assert.True(
                Make.Map(new FakeGraph(), roles: new() { [Guid.NewGuid().ToString()] = "A" }).IsConfigured
            );

        [Fact]
        public void NamesAloneAreConfigured() =>
            Assert.True(Make.Map(new FakeGraph(), null, null, "A").IsConfigured);

        [Theory]
        [InlineData("D")]
        [InlineData("N")]
        [InlineData("B")]
        public async Task StaticObjectIdAcceptsAnyGuidFormat(string format)
        {
            var graph = new FakeGraph();
            var id = Guid.NewGuid();
            var map = await Make.Map(graph, roles: new() { [id.ToString(format).ToUpperInvariant()] = "Admins" })
                .GetAsync();

            Assert.Equal("Admins", map[id]);
            Assert.Equal(0, graph.Lookups);
        }

        [Fact]
        public async Task StaticKeyThatIsNotAnObjectIdFails()
        {
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new FakeGraph(), roles: new() { ["Admins"] = "Admins" }).GetAsync()
            );
            Assert.Contains("not a group Object ID", ex.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task StaticEntryWithoutRoleFails(string role)
        {
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new FakeGraph(), roles: new() { [Guid.NewGuid().ToString()] = role }).GetAsync()
            );
            Assert.Contains("has no role name", ex.Message);
        }

        [Fact]
        public async Task TwoStaticGroupsMayShareARole()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var map = await Make.Map(
                    new FakeGraph(),
                    roles: new() { [a.ToString()] = "Admins", [b.ToString()] = "Admins" }
                )
                .GetAsync();
            Assert.Equal("Admins", map[a]);
            Assert.Equal("Admins", map[b]);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task EmptyGroupNameFails(string name)
        {
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new FakeGraph(), null, null, name).GetAsync()
            );
            Assert.Contains("empty name", ex.Message);
        }

        [Fact]
        public async Task DiscoversAndStoresUniqueName()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            graph.Add("Users");
            var store = new MemoryGroupRoleMapStore();

            var map = await Make.Map(graph, store, null, "Admins").GetAsync();

            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
            Assert.Single(map);
            Assert.Equal(Guid.Parse(admins.Id!), (await store.LoadAsync(default))["Admins"]);
        }

        [Fact]
        public async Task RoleIsTheNameAsConfigured()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("ADMINS");
            var map = await Make.Map(graph, null, null, "Admins").GetAsync();
            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
        }

        [Fact]
        public async Task RepeatedNamesAreLookedUpOnce()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var map = await Make.Map(graph, null, null, "Admins", "admins", "Admins").GetAsync();
            Assert.Single(map);
            Assert.Equal(1, graph.Lookups);
        }

        [Fact]
        public async Task NameMatchingNoGroupFails()
        {
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new FakeGraph(), null, null, "Admins").GetAsync()
            );
            Assert.Contains("No security group is named 'Admins'", ex.Message);
        }

        [Fact]
        public async Task NameMatchingTwoGroupsFailsAndListsThem()
        {
            var graph = new FakeGraph();
            var a = graph.Add("Admins");
            var b = graph.Add("Admins");

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, null, "Admins").GetAsync()
            );
            Assert.Contains("2 security groups are named 'Admins'", ex.Message);
            Assert.Contains(a.Id!, ex.Message);
            Assert.Contains(b.Id!, ex.Message);
        }

        [Fact]
        public async Task NamesDifferingOnlyInCaseAreDuplicates()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            graph.Add("admins");
            await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, null, "Admins").GetAsync()
            );
        }

        [Fact]
        public async Task GroupWithoutValidObjectIdFails()
        {
            var graph = new FakeGraph();
            graph.Directory.Add(new Group { Id = "not-a-guid", DisplayName = "Admins" });
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, null, "Admins").GetAsync()
            );
            Assert.Contains("no valid Object ID", ex.Message);
        }

        [Fact]
        public async Task FailedDiscoveryStoresNothing()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            graph.Add("Editors");
            graph.Add("Editors");
            var store = new MemoryGroupRoleMapStore();

            await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, store, null, "Admins", "Editors").GetAsync()
            );
            Assert.Empty(await store.LoadAsync(default));
        }

        [Fact]
        public async Task StoredNameIgnoresLaterDuplicate()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            var store = new MemoryGroupRoleMapStore();
            await Make.Map(graph, store, null, "Admins").GetAsync();

            graph.Add("Admins");
            var map = await Make.Map(graph, store, null, "Admins").GetAsync();

            Assert.Equal(Guid.Parse(admins.Id!), map.Keys.Single());
            Assert.Equal(1, graph.Lookups);
        }

        [Fact]
        public async Task StoredNameSurvivesGroupRename()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            var store = new MemoryGroupRoleMapStore();
            await Make.Map(graph, store, null, "Admins").GetAsync();

            admins.DisplayName = "Former Admins";
            var map = await Make.Map(graph, store, null, "Admins").GetAsync();
            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
        }

        [Fact]
        public async Task OnlyNamesMissingFromTheStoreAreLookedUp()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var editors = graph.Add("Editors");
            var store = new MemoryGroupRoleMapStore();
            await store.SaveAsync(new Dictionary<string, Guid> { ["Admins"] = Guid.NewGuid() }, default);

            var map = await Make.Map(graph, store, null, "Admins", "Editors").GetAsync();

            Assert.Equal(2, map.Count);
            Assert.Equal("Editors", map[Guid.Parse(editors.Id!)]);
            Assert.Equal(1, graph.Lookups);
        }

        [Fact]
        public async Task NameCoveredByStaticRoleIsNotLookedUp()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            graph.Add("Admins");
            var id = Guid.NewGuid();

            var map = await Make.Map(graph, null, new() { [id.ToString()] = "Admins" }, "ADMINS").GetAsync();

            Assert.Equal("Admins", Assert.Single(map).Value);
            Assert.Equal(0, graph.Lookups);
        }

        [Fact]
        public async Task ObjectIdMappedToTwoRolesFails()
        {
            var graph = new FakeGraph();
            var editors = graph.Add("Editors");
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, new() { [editors.Id!] = "Admins" }, "Editors").GetAsync()
            );
            Assert.Contains("mapped to both", ex.Message);
        }

        [Fact]
        public async Task StoreThatLosesDiscoveredNameFails()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, new ForgetfulStore(), null, "Admins").GetAsync()
            );
            Assert.Contains("missing from the group role map store", ex.Message);
        }

        [Fact]
        public async Task MapIsBuiltOnce()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var map = Make.Map(graph, null, null, "Admins");

            var first = await map.GetAsync();
            var second = await map.GetAsync();

            Assert.Same(first, second);
            Assert.Equal(1, graph.Lookups);
        }

        [Fact]
        public async Task ConcurrentCallersShareOneBuild()
        {
            var graph = new FakeGraph { Delay = TimeSpan.FromMilliseconds(50) };
            graph.Add("Admins");
            var map = Make.Map(graph, null, null, "Admins");

            var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => map.GetAsync()));

            Assert.All(results, x => Assert.Same(results[0], x));
            Assert.Equal(1, graph.Lookups);
        }

        [Fact]
        public async Task FailedBuildIsRetried()
        {
            var graph = new FakeGraph();
            var map = Make.Map(graph, null, null, "Admins");
            await Assert.ThrowsAsync<GroupRoleMapException>(() => map.GetAsync());

            var admins = graph.Add("Admins");
            Assert.Equal("Admins", (await map.GetAsync())[Guid.Parse(admins.Id!)]);
        }

        static GroupRoleMap MapWithStoreFactory(FakeGraph graph, Func<IGroupRoleMapStore> store, Dictionary<string, string> roles, params string[] names) =>
            new(
                Microsoft.Extensions.Options.Options.Create(
                    new GraphUtilityServiceOptions { GroupRoles = roles, GroupNames = names.ToList() }
                ),
                graph,
                store,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GroupRoleMap>.Instance
            );

        [Fact]
        public async Task StaticRolesNeverCreateTheStore()
        {
            var id = Guid.NewGuid();
            var map = await MapWithStoreFactory(
                    new FakeGraph(),
                    () => throw new InvalidOperationException("store created"),
                    new() { [id.ToString()] = "Admins" }
                )
                .GetAsync();
            Assert.Equal("Admins", map[id]);
        }

        [Fact]
        public async Task NamesCoveredByStaticRolesNeverCreateTheStore()
        {
            var id = Guid.NewGuid();
            var map = await MapWithStoreFactory(
                    new FakeGraph(),
                    () => throw new InvalidOperationException("store created"),
                    new() { [id.ToString()] = "Admins" },
                    "Admins"
                )
                .GetAsync();
            Assert.Equal("Admins", map[id]);
        }

        [Fact]
        public async Task NameToResolveCreatesTheStoreOnce()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var created = 0;
            var map = MapWithStoreFactory(
                graph,
                () =>
                {
                    created++;
                    return new MemoryGroupRoleMapStore();
                },
                new(),
                "Admins"
            );
            await map.GetAsync();
            await map.GetAsync();
            Assert.Equal(1, created);
        }
    }
}
