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
        public async Task StoredNameSurvivesGroupRenameWhenNotVerified()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            var store = new MemoryGroupRoleMapStore();
            await Make.Map(graph, store, null, "Admins").GetAsync();

            admins.DisplayName = "Former Admins";
            var map = await Make.Map(graph, store, null, new[] { "Admins" }, o => o.GroupRoleMapVerifyStored = false)
                .GetAsync();
            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
            Assert.Equal(0, graph.IdLookups);
        }

        [Fact]
        public async Task OnlyNamesMissingFromTheStoreAreLookedUp()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            var editors = graph.Add("Editors");
            var store = new MemoryGroupRoleMapStore();
            await store.SaveAsync(
                new Dictionary<string, Guid> { ["Admins"] = Guid.Parse(graph.Directory[0].Id!) },
                default
            );

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

        // ---- stored IDs are checked against Entra ID at startup

        static async Task<MemoryGroupRoleMapStore> StoreWith(string name, Guid id)
        {
            var store = new MemoryGroupRoleMapStore();
            await store.SaveAsync(new Dictionary<string, Guid> { [name] = id }, default);
            return store;
        }

        [Fact]
        public async Task StoredIdIsVerifiedByDefault()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            var map = await Make.Map(graph, await StoreWith("Admins", Guid.Parse(admins.Id!)), null, "Admins")
                .GetAsync();

            Assert.Equal("Admins", map[Guid.Parse(admins.Id!)]);
            Assert.Equal(1, graph.IdLookups);
            Assert.Equal(0, graph.Lookups);
        }

        [Fact]
        public async Task StoredIdMatchesNameIgnoringCase()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("ADMINS");
            await Make.Map(graph, await StoreWith("Admins", Guid.Parse(admins.Id!)), null, "Admins").GetAsync();
        }

        [Fact]
        public async Task StoredIdMissingFromEntraFails()
        {
            var id = Guid.NewGuid();
            var store = await StoreWith("Admins", id);
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new FakeGraph(), store, null, "Admins").GetAsync()
            );
            Assert.Contains($"Stored group 'Admins' ({id}) does not exist", ex.Message);
        }

        [Fact]
        public async Task StoredIdOfRenamedGroupFails()
        {
            var graph = new FakeGraph();
            var admins = graph.Add("Admins");
            admins.DisplayName = "Former Admins";
            var stored = await StoreWith("Admins", Guid.Parse(admins.Id!));
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, stored, null, "Admins").GetAsync()
            );
            Assert.Contains("is named 'Former Admins'", ex.Message);
        }

        [Fact]
        public async Task StoredIdPointingAtOtherGroupFails()
        {
            // a store row rewritten to an attacker's group with a different name
            var graph = new FakeGraph();
            graph.Add("Admins");
            var mine = graph.Add("My Group");
            var stored = await StoreWith("Admins", Guid.Parse(mine.Id!));
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, stored, null, "Admins").GetAsync()
            );
            Assert.Contains("is named 'My Group'", ex.Message);
        }

        [Theory]
        [InlineData(false, null)]
        [InlineData(true, "Unified")]
        public async Task StoredIdOfNonSecurityGroupFails(bool securityEnabled, string? groupType)
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins");
            group.SecurityEnabled = securityEnabled;
            if (groupType != null)
                group.GroupTypes = new List<string> { groupType };
            var stored = await StoreWith("Admins", Guid.Parse(group.Id!));
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, stored, null, "Admins").GetAsync()
            );
            Assert.Contains("is not a security group", ex.Message);
        }

        [Fact]
        public async Task StoredIdsAreCheckedInOneCall()
        {
            var graph = new FakeGraph();
            var store = new MemoryGroupRoleMapStore();
            var names = Enumerable.Range(0, 20).Select(i => $"G{i}").ToArray();
            await store.SaveAsync(
                names.ToDictionary(x => x, x => Guid.Parse(graph.Add(x).Id!)),
                default
            );

            var map = await Make.Map(graph, store, null, names).GetAsync();

            Assert.Equal(20, map.Count);
            Assert.Equal(new[] { 20 }, graph.IdBatchSizes);
        }

        [Fact]
        public async Task JustDiscoveredNamesAreNotCheckedAgain()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            await Make.Map(graph, null, null, "Admins").GetAsync();
            Assert.Equal(0, graph.IdLookups);
        }

        [Fact]
        public async Task NameStoredByAnotherInstanceMeanwhileIsChecked()
        {
            // our lookup found one group, but another instance stored a different ID first
            var graph = new FakeGraph();
            var ours = graph.Add("Admins");
            var theirs = Guid.NewGuid();
            var store = new RaceStore("Admins", theirs);

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, store, null, "Admins").GetAsync()
            );
            Assert.Contains($"({theirs}) does not exist", ex.Message);
            Assert.Equal(1, graph.IdLookups);
        }

        /// <summary>Empty on first load, then shows a row another instance wrote.</summary>
        class RaceStore : IGroupRoleMapStore
        {
            readonly string Name;
            readonly Guid Id;
            bool Saved;

            public RaceStore(string name, Guid id)
            {
                Name = name;
                Id = id;
            }

            public Task<IReadOnlyDictionary<string, Guid>> LoadAsync(System.Threading.CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<string, Guid>>(
                    Saved
                        ? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { [Name] = Id }
                        : new Dictionary<string, Guid>()
                );

            public Task SaveAsync(IReadOnlyDictionary<string, Guid> entries, System.Threading.CancellationToken cancellationToken)
            {
                Saved = true;
                return Task.CompletedTask;
            }
        }

        // ---- role-assignable requirement

        [Fact]
        public async Task RoleAssignableIsNotRequiredByDefault()
        {
            var graph = new FakeGraph();
            graph.Add("Admins", roleAssignable: false);
            Assert.Single(await Make.Map(graph, null, null, "Admins").GetAsync());
        }

        [Fact]
        public async Task RequiredRoleAssignableRejectsOrdinaryGroupAtLookup()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins", roleAssignable: false);
            var store = new MemoryGroupRoleMapStore();

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, store, null, new[] { "Admins" }, o => o.GroupRoleMapRequireRoleAssignable = true)
                    .GetAsync()
            );
            Assert.Contains($"'Admins' ({group.Id}) is not role-assignable", ex.Message);
            Assert.Empty(await store.LoadAsync(default));
        }

        [Fact]
        public async Task RequiredRoleAssignableAcceptsRoleAssignableGroup()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins", roleAssignable: true);
            var map = await Make.Map(graph, null, null, new[] { "Admins" }, o => o.GroupRoleMapRequireRoleAssignable = true)
                .GetAsync();
            Assert.Equal("Admins", map[Guid.Parse(group.Id!)]);
        }

        [Fact]
        public async Task RequiredRoleAssignableRejectsStoredOrdinaryGroup()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins", roleAssignable: false);
            var stored = await StoreWith("Admins", Guid.Parse(group.Id!));
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(
                        graph,
                        stored,
                        null,
                        new[] { "Admins" },
                        o => o.GroupRoleMapRequireRoleAssignable = true
                    )
                    .GetAsync()
            );
            Assert.Contains("Stored group 'Admins'", ex.Message);
            Assert.Contains("not role-assignable", ex.Message);
        }

        [Fact]
        public async Task RoleAssignableDoesNotApplyToStaticRoles()
        {
            var id = Guid.NewGuid();
            var map = await Make.Map(
                    new FakeGraph(),
                    null,
                    new() { [id.ToString()] = "Admins" },
                    Array.Empty<string>(),
                    o => o.GroupRoleMapRequireRoleAssignable = true
                )
                .GetAsync();
            Assert.Equal("Admins", map[id]);
        }

        // ---- logging

        [Fact]
        public async Task NewLookupIsLoggedAsWarningWithGroupDetails()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins", roleAssignable: true);
            var logger = new ListLogger<GroupRoleMap>();

            await Make.Map(graph, null, null, new[] { "Admins" }, null, logger).GetAsync();

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
            Assert.Contains(group.Id!, entry.Message);
            Assert.Contains("role-assignable True", entry.Message);
            Assert.Contains("created", entry.Message);
        }

        [Fact]
        public async Task StoredMappingIsLoggedAsInformation()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins");
            var logger = new ListLogger<GroupRoleMap>();

            await Make.Map(graph, await StoreWith("Admins", Guid.Parse(group.Id!)), null, new[] { "Admins" }, null, logger)
                .GetAsync();

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Information, entry.Level);
            Assert.Contains("checked against Entra ID", entry.Message);
        }

        // ---- dynamic membership groups are ignored with a warning

        static Group Dynamic(FakeGraph graph, string name)
        {
            var group = graph.Add(name);
            group.GroupTypes = new List<string> { "DynamicMembership" };
            return group;
        }

        static bool Warned(ListLogger<GroupRoleMap> logger, string id) =>
            logger.Entries.Any(x =>
                x.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                && x.Message.Contains(id)
                && x.Message.Contains("dynamic membership")
            );

        [Fact]
        public async Task OnlyDynamicMatchIsNoMatch()
        {
            var graph = new FakeGraph();
            var group = Dynamic(graph, "Admins");
            var logger = new ListLogger<GroupRoleMap>();
            var store = new MemoryGroupRoleMapStore();

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, store, null, new[] { "Admins" }, null, logger).GetAsync()
            );
            Assert.Contains("No security group with assigned membership is named 'Admins'", ex.Message);
            Assert.Contains("1 with dynamic membership were ignored", ex.Message);
            Assert.True(Warned(logger, group.Id!));
            Assert.Empty(await store.LoadAsync(default));
        }

        [Fact]
        public async Task DynamicGroupWithSameNameIsNotADuplicate()
        {
            var graph = new FakeGraph();
            var assigned = graph.Add("Admins");
            var dynamic = Dynamic(graph, "Admins");
            var logger = new ListLogger<GroupRoleMap>();

            var map = await Make.Map(graph, null, null, new[] { "Admins" }, null, logger).GetAsync();

            Assert.Equal("Admins", Assert.Single(map).Value);
            Assert.Equal(Guid.Parse(assigned.Id!), map.Keys.Single());
            Assert.True(Warned(logger, dynamic.Id!));
        }

        [Fact]
        public async Task TwoAssignedGroupsAreStillDuplicatesBesideADynamicOne()
        {
            var graph = new FakeGraph();
            graph.Add("Admins");
            graph.Add("Admins");
            Dynamic(graph, "Admins");

            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, null, "Admins").GetAsync()
            );
            Assert.Contains("2 security groups are named 'Admins'", ex.Message);
        }

        [Fact]
        public async Task DynamicGroupTypeMatchesInAnyCase()
        {
            var graph = new FakeGraph();
            graph.Add("Admins").GroupTypes = new List<string> { "dynamicmembership" };
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(graph, null, null, "Admins").GetAsync()
            );
            Assert.Contains("dynamic membership were ignored", ex.Message);
        }

        [Fact]
        public async Task StoredDynamicGroupIsLeftOutWithWarningAndStartupContinues()
        {
            var graph = new FakeGraph();
            var admins = Dynamic(graph, "Admins");
            var editors = graph.Add("Editors");
            var store = new MemoryGroupRoleMapStore();
            await store.SaveAsync(
                new Dictionary<string, Guid>
                {
                    ["Admins"] = Guid.Parse(admins.Id!),
                    ["Editors"] = Guid.Parse(editors.Id!),
                },
                default
            );
            var logger = new ListLogger<GroupRoleMap>();

            var map = await Make.Map(graph, store, null, new[] { "Admins", "Editors" }, null, logger).GetAsync();

            Assert.Equal("Editors", Assert.Single(map).Value);
            Assert.True(Warned(logger, admins.Id!));
            // the stored row is kept, so the name is not looked up again
            Assert.Equal(Guid.Parse(admins.Id!), (await store.LoadAsync(default))["Admins"]);
            Assert.Equal(0, graph.Lookups);
        }

        [Fact]
        public async Task GroupChangedToDynamicAfterStoringStopsGrantingTheRole()
        {
            var graph = new FakeGraph();
            var group = graph.Add("Admins");
            var store = new MemoryGroupRoleMapStore();
            Assert.Single(await Make.Map(graph, store, null, "Admins").GetAsync());

            group.GroupTypes = new List<string> { "DynamicMembership" };
            Assert.Empty(await Make.Map(graph, store, null, "Admins").GetAsync());
        }

        [Fact]
        public async Task TransformGrantsNoRoleForIgnoredDynamicGroup()
        {
            var graph = new FakeGraph();
            var group = Dynamic(graph, "Admins");
            var stored = await StoreWith("Admins", Guid.Parse(group.Id!));
            var transform = new AzureGroupsClaimsTransform(
                Make.Map(graph, stored, null, "Admins"),
                Microsoft.Extensions.Options.Options.Create(new GraphUtilityServiceOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance
            );

            var result = await transform.TransformAsync(
                Make.Principal(new System.Security.Claims.Claim("groups", group.Id!))
            );
            Assert.False(result.IsInRole("Admins"));
        }

        [Fact]
        public async Task StoredDynamicGroupIsNotSeenWhenVerificationIsOff()
        {
            var graph = new FakeGraph();
            var group = Dynamic(graph, "Admins");
            var stored = await StoreWith("Admins", Guid.Parse(group.Id!));

            var map = await Make.Map(graph, stored, null, new[] { "Admins" }, o => o.GroupRoleMapVerifyStored = false)
                .GetAsync();
            Assert.Single(map);
        }

        [Fact]
        public async Task DynamicCheckDoesNotApplyToStaticRoles()
        {
            var id = Guid.NewGuid();
            var map = await Make.Map(new FakeGraph(), roles: new() { [id.ToString()] = "Admins" }).GetAsync();
            Assert.Equal("Admins", map[id]);
        }
    }
}
