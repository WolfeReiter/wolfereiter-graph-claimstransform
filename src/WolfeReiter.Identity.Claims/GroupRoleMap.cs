using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                var discovered = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in names.Where(x => !stored.ContainsKey(x)))
                    discovered[name] = await DiscoverAsync(name);

                if (discovered.Count > 0)
                {
                    await Store.Value.SaveAsync(discovered, cancellationToken);
                    stored = await Store.Value.LoadAsync(cancellationToken);
                }

                foreach (var name in names)
                {
                    if (!stored.TryGetValue(name, out var id))
                        throw new GroupRoleMapException(
                            $"Group '{name}' was discovered but is missing from the group role map store."
                        );
                    if (map.TryGetValue(id, out var existing))
                        throw new GroupRoleMapException(
                            $"Group {id} is mapped to both '{existing}' and '{name}'."
                        );
                    map[id] = name;
                    Logger.LogInformation(
                        "Group '{GroupName}' maps to Object ID {ObjectId}{Source}.",
                        name,
                        id,
                        discovered.ContainsKey(name) ? ", discovered and stored" : " from the store"
                    );
                }
            }

            return map;
        }

        private async Task<Guid> DiscoverAsync(string name)
        {
            var groups = (await GraphService.SecurityGroupsByDisplayNameAsync(name)).ToList();
            if (groups.Count == 0)
                throw new GroupRoleMapException($"No security group is named '{name}'.");
            if (groups.Count > 1)
                throw new GroupRoleMapException(
                    $"{groups.Count} security groups are named '{name}' ({string.Join(", ", groups.Select(x => x.Id))}). "
                        + "Rename the duplicates or map the intended Object ID in GroupRoles."
                );
            if (!Guid.TryParse(groups[0].Id, out var id))
                throw new GroupRoleMapException(
                    $"Security group '{name}' has no valid Object ID."
                );
            return id;
        }
    }
}
