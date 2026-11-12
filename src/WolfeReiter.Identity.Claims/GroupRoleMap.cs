using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// Map of security group Object ID to role claim value, built from
    /// <see cref="GraphUtilityServiceOptions.GroupRoles"/> and
    /// <see cref="GraphUtilityServiceOptions.GroupNames"/>.
    /// </summary>
    public class GroupRoleMap
    {
        private readonly GraphUtilityServiceOptions Options;
        private readonly IGraphUtilityService GraphService;
        private readonly Lazy<IGroupRoleMapStore> Store;
        private readonly ILogger Logger;
        private readonly SemaphoreSlim Gate = new(1, 1);
        private IReadOnlyDictionary<Guid, string>? Map;

        public GroupRoleMap(
            IOptions<GraphUtilityServiceOptions> options,
            IGraphUtilityService graphService,
            IGroupRoleMapStore store,
            ILogger<GroupRoleMap> logger
        )
            : this(options, graphService, () => store, logger) { }

        /// <summary>
        /// The store is created only when a group name has to be resolved, so a map built
        /// entirely from GroupRoles never touches it.
        /// </summary>
        public GroupRoleMap(
            IOptions<GraphUtilityServiceOptions> options,
            IGraphUtilityService graphService,
            Func<IGroupRoleMapStore> store,
            ILogger<GroupRoleMap> logger
        )
        {
            Options = options.Value;
            GraphService = graphService;
            Store = new Lazy<IGroupRoleMapStore>(store);
            Logger = logger;
        }

        /// <summary>
        /// False when neither GroupRoles nor GroupNames is set, which stops startup.
        /// </summary>
        public bool IsConfigured => Options.GroupRoles.Count > 0 || Options.GroupNames.Count > 0;

        /// <summary>
        /// Builds the map on first call, discovering and storing Object IDs for group names not
        /// already stored. Throws <see cref="GroupRoleMapException"/> when a name matches no
        /// security group or more than one.
        /// </summary>
        public async Task<IReadOnlyDictionary<Guid, string>> GetAsync(
            CancellationToken cancellationToken = default
        )
        {
            if (Map != null)
                return Map;
            if (!IsConfigured)
                throw new GroupRoleMapException(
                    "No group names were passed to AddWolfeReiterAzureGroupsClaimsTransform and neither GroupRoles nor GroupNames is configured, so no group can be mapped to a role."
                );
            await Gate.WaitAsync(cancellationToken);
            try
            {
                return Map ??= await BuildAsync(cancellationToken);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<IReadOnlyDictionary<Guid, string>> BuildAsync(
            CancellationToken cancellationToken
        )
        {
            var map = new Dictionary<Guid, string>();

            foreach (var (key, role) in Options.GroupRoles)
            {
                if (!Guid.TryParse(key, out var id))
                    throw new GroupRoleMapException(
                        $"GroupRoles key '{key}' is not a group Object ID."
                    );
                if (string.IsNullOrWhiteSpace(role))
                    throw new GroupRoleMapException(
                        $"GroupRoles entry for {id} has no role name."
                    );
                map[id] = role;
            }

            var staticRoles = new HashSet<string>(map.Values, StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            foreach (var name in Options.GroupNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new GroupRoleMapException("GroupNames contains an empty name.");
                if (
                    !staticRoles.Contains(name)
                    && !names.Contains(name, StringComparer.OrdinalIgnoreCase)
                )
                    names.Add(name);
            }

            if (names.Count > 0)
            {
                var stored = await Store.Value.LoadAsync(cancellationToken);
                var discovered = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names.Where(x => !stored.ContainsKey(x)))
                    discovered[name] = await DiscoverAsync(name);

                if (discovered.Count > 0)
                {
                    await Store.Value.SaveAsync(
                        discovered.ToDictionary(
                            x => x.Key,
                            x => Guid.Parse(x.Value.Id!),
                            StringComparer.OrdinalIgnoreCase
                        ),
                        cancellationToken
                    );
                    stored = await Store.Value.LoadAsync(cancellationToken);
                }

                foreach (var name in names)
                {
                    if (!stored.ContainsKey(name))
                        throw new GroupRoleMapException(
                            $"Group '{name}' was discovered but is missing from the group role map store."
                        );
                }

                // every stored ID not just checked by discovery, including any written by another
                // instance in the meantime
                var toVerify = names
                    .Where(x =>
                        !discovered.TryGetValue(x, out var group)
                        || Guid.Parse(group.Id!) != stored[x]
                    )
                    .ToList();
                var excluded = Options.GroupRoleMapVerifyStored && toVerify.Count > 0
                    ? await VerifyStoredAsync(toVerify, stored)
                    : new HashSet<string>();

                foreach (var name in names.Where(x => !excluded.Contains(x)))
                {
                    var id = stored[name];
                    if (map.TryGetValue(id, out var existing))
                        throw new GroupRoleMapException(
                            $"Group {id} is mapped to both '{existing}' and '{name}'."
                        );
                    map[id] = name;

                    if (discovered.TryGetValue(name, out var group) && Guid.Parse(group.Id!) == id)
                        Logger.LogWarning(
                            "Group name '{GroupName}' was looked up in Entra ID and is now mapped to Object ID {ObjectId}, created {CreatedDateTime}, role-assignable {RoleAssignable}. Confirm this is the intended group.",
                            name,
                            id,
                            group.CreatedDateTime,
                            group.IsAssignableToRole == true
                        );
                    else
                        Logger.LogInformation(
                            "Group '{GroupName}' maps to Object ID {ObjectId} from the store{Verified}.",
                            name,
                            id,
                            Options.GroupRoleMapVerifyStored ? ", checked against Entra ID" : ""
                        );
                }
            }

            return map;
        }

        /// <summary>True when the store was needed and is the in-memory one.</summary>
        internal bool UsedMemoryStore => Store.IsValueCreated && Store.Value is MemoryGroupRoleMapStore;

        private async Task<Group> DiscoverAsync(string name)
        {
            var found = (await GraphService.SecurityGroupsByDisplayNameAsync(name)).ToList();
            // dynamic groups take members from a rule over user attributes, so they never count
            var dynamic = found.Where(GroupKind.IsDynamic).ToList();
            foreach (var group in dynamic)
                Logger.LogWarning(
                    "Security group '{GroupName}' ({ObjectId}) has dynamic membership and is ignored.",
                    group.DisplayName,
                    group.Id
                );
            var groups = found.Except(dynamic).ToList();
            if (groups.Count == 0)
                throw new GroupRoleMapException(
                    dynamic.Count == 0
                        ? $"No security group is named '{name}'."
                        : $"No security group with assigned membership is named '{name}'. {dynamic.Count} with dynamic membership were ignored."
                );
            if (groups.Count > 1)
                throw new GroupRoleMapException(
                    $"{groups.Count} security groups are named '{name}' ({string.Join(", ", groups.Select(x => x.Id))}). "
                        + "Rename the duplicates or map the intended Object ID in GroupRoles."
                );
            var match = groups[0];
            if (!Guid.TryParse(match.Id, out _))
                throw new GroupRoleMapException(
                    $"Security group '{name}' has no valid Object ID."
                );
            if (Options.GroupRoleMapRequireRoleAssignable && match.IsAssignableToRole != true)
                throw new GroupRoleMapException(
                    $"Security group '{name}' ({match.Id}) is not role-assignable, which GroupRoleMapRequireRoleAssignable requires."
                );
            return match;
        }

        /// <summary>
        /// Checks stored IDs against Entra ID and returns the names whose group now has dynamic
        /// membership, which are left out of the map.
        /// </summary>
        private async Task<HashSet<string>> VerifyStoredAsync(
            IReadOnlyList<string> names,
            IReadOnlyDictionary<string, Guid> stored
        )
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<Guid, Group>();
            foreach (var group in await GraphService.GroupsByIdAsync(names.Select(x => stored[x])))
            {
                if (Guid.TryParse(group.Id, out var id))
                    found[id] = group;
            }

            foreach (var name in names)
            {
                var id = stored[name];
                if (!found.TryGetValue(id, out var group))
                    throw new GroupRoleMapException(
                        $"Stored group '{name}' ({id}) does not exist in Entra ID."
                    );
                if (!GroupKind.IsSecurityGroup(group))
                    throw new GroupRoleMapException(
                        $"Stored group '{name}' ({id}) is not a security group."
                    );
                if (!string.Equals(group.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    throw new GroupRoleMapException(
                        $"Stored group '{name}' ({id}) is named '{group.DisplayName}' in Entra ID. "
                            + "Correct the group or the store entry."
                    );
                if (Options.GroupRoleMapRequireRoleAssignable && group.IsAssignableToRole != true)
                    throw new GroupRoleMapException(
                        $"Stored group '{name}' ({id}) is not role-assignable, which GroupRoleMapRequireRoleAssignable requires."
                    );
                if (GroupKind.IsDynamic(group))
                {
                    Logger.LogWarning(
                        "Stored group '{GroupName}' ({ObjectId}) has dynamic membership, so it is ignored and grants no role.",
                        name,
                        id
                    );
                    excluded.Add(name);
                }
            }
            return excluded;
        }
    }
}
