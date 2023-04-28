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

        public async Task<IEnumerable<Group>?> GroupsAsync(ClaimsPrincipal principal)
        {
            var oid = principal.GetObjectId();
            if (oid == null)
                return null; //no objectidentifer means not an AzureAD ClaimsPrincipal

            var credential = new ClientSecretCredential(
                Options.TenantId,
                Options.ClientId,
                Options.ClientSecret,
                new TokenCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzurePublicCloud }
            );

            var graphServiceClient = NewAuthenticatedClient(NewClientSecretCredential());

            var memberOfGroups = await graphServiceClient.Users[oid].MemberOf.GetAsync();

            var groups = await AllPagesAsync<
                DirectoryObject,
                DirectoryObjectCollectionResponse,
                Group
            >(graphServiceClient, memberOfGroups);
            return groups;
        }
    }
}
