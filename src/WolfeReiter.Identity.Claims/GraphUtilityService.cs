using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    public class GraphUtilityService : GraphUtilityServiceBase, IGraphUtilityService
    {
        private readonly GraphUtilityServiceOptions Options;
        public GraphUtilityService(IOptions<GraphUtilityServiceOptions> options) : base(options) 
        { 
            Options = options.Value;
        }

        public async Task<IEnumerable<Group>?> GroupsAsync(ClaimsPrincipal principal, string accessToken) 
        {
            var oid = principal.GetObjectId();
            if (oid == null) return null; //no objectidentifer means not an AzureAD ClaimsPrincipal

            var graphServiceClient = NewAuthenticatedClient(accessToken);

            var memberOfGroups = await graphServiceClient.Users[oid].MemberOf
                .Request()
                .GetAsync();

            var groups = await AllPagesAsync<DirectoryObject, Group>(graphServiceClient, memberOfGroups);
            return groups;
        }
    }
}