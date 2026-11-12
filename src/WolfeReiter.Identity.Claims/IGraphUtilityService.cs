using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Graph.Models;

namespace WolfeReiter.Identity.Claims
{
    public interface IGraphUtilityService
    {
        [Obsolete("Role claims come from the token groups claim and Entra ID is no longer queried at sign in.", error: true)]
        Task<IEnumerable<Group>?> GroupsAsync(ClaimsPrincipal principal) =>
            throw new NotSupportedException("Role claims come from the token groups claim and Entra ID is no longer queried at sign in.");

        /// <summary>
        /// Every security group whose display name matches <paramref name="displayName"/>.
        /// Microsoft 365 groups are excluded.
        /// </summary>
        Task<IEnumerable<Group>> SecurityGroupsByDisplayNameAsync(string displayName) =>
            throw new NotSupportedException(
                $"{GetType().Name} does not implement SecurityGroupsByDisplayNameAsync, which GroupNames discovery requires."
            );

        /// <summary>
        /// The groups with these Object IDs, of any kind. IDs with no group are left out.
        /// </summary>
        Task<IEnumerable<Group>> GroupsByIdAsync(IEnumerable<Guid> ids) =>
            throw new NotSupportedException(
                $"{GetType().Name} does not implement GroupsByIdAsync, which GroupRoleMapVerifyStored requires."
            );
    }
}
