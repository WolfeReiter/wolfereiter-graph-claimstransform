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

        public async Task<IEnumerable<Group>> SecurityGroupsByDisplayNameAsync(string displayName)
        {
            var graphServiceClient = NewAuthenticatedClient(NewClientSecretCredential());
            var escaped = displayName.Replace("'", "''");
            var page = await graphServiceClient.Groups.GetAsync(request =>
            {
                request.QueryParameters.Filter = $"displayName eq '{escaped}'";
                request.QueryParameters.Select = new[]
                {
                    "id",
                    "displayName",
                    "securityEnabled",
                    "groupTypes",
                };
            });

            var groups = await AllPagesAsync<Group, GroupCollectionResponse, Group>(
                graphServiceClient,
                page
            );
            return groups?.Where(IsSecurityGroup).ToList() ?? new List<Group>();
        }

        /// <summary>
        /// True for security groups. Microsoft 365 groups have "Unified" in GroupTypes and are
        /// excluded even when they are also security enabled.
        /// </summary>
        private static bool IsSecurityGroup(Group group) =>
            group.SecurityEnabled == true
            && !(group.GroupTypes?.Contains("Unified", StringComparer.OrdinalIgnoreCase) ?? false);
    }
}
