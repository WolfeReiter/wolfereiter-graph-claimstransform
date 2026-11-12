using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityService : GraphUtilityServiceBase, IGraphUtilityService
    {
        private readonly GraphUtilityServiceOptions Options;

        public GraphUtilityService(IOptions<GraphUtilityServiceOptions> options)
            : base(options)
        {
            Options = options.Value;
        }

        internal static readonly string[] GroupFields =
        {
            "id",
            "displayName",
            "securityEnabled",
            "groupTypes",
            "isAssignableToRole",
            "createdDateTime",
        };

        public async Task<IEnumerable<Group>> SecurityGroupsByDisplayNameAsync(string displayName)
        {
            var groups = await QueryGroupsAsync($"displayName eq '{displayName.Replace("'", "''")}'");
            return groups.Where(GroupKind.IsSecurityGroup).ToList();
        }

        public async Task<IEnumerable<Group>> GroupsByIdAsync(IEnumerable<Guid> ids)
        {
            var result = new List<Group>();
            // Graph allows up to 15 values in an "in" filter
            foreach (var chunk in ids.Distinct().Chunk(15))
            {
                var list = string.Join(",", chunk.Select(x => $"'{x:D}'"));
                result.AddRange(await QueryGroupsAsync($"id in ({list})"));
            }
            return result;
        }

        private async Task<List<Group>> QueryGroupsAsync(string filter)
        {
            var graphServiceClient = NewAuthenticatedClient(NewClientSecretCredential());
            var page = await graphServiceClient.Groups.GetAsync(request =>
            {
                request.QueryParameters.Filter = filter;
                request.QueryParameters.Select = GroupFields;
            });

            var groups = await AllPagesAsync<Group, GroupCollectionResponse, Group>(
                graphServiceClient,
                page
            );
            return groups?.ToList() ?? new List<Group>();
        }
    }

    internal static class GroupKind
    {
        /// <summary>
        /// True for security groups. Microsoft 365 groups have "Unified" in GroupTypes and are
        /// excluded even when they are also security enabled.
        /// </summary>
        public static bool IsSecurityGroup(Group group) =>
            group.SecurityEnabled == true
            && !(group.GroupTypes?.Contains("Unified", StringComparer.OrdinalIgnoreCase) ?? false);

        /// <summary>True when membership is set by a rule rather than assigned.</summary>
        public static bool IsDynamic(Group group) =>
            group.GroupTypes?.Contains("DynamicMembership", StringComparer.OrdinalIgnoreCase) ?? false;
    }
}
