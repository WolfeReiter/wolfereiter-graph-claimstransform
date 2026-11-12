using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    /// <summary>Answers Graph requests from a queue of JSON bodies and records the requests.</summary>
    class StubGraphHandler : HttpMessageHandler
    {
        public Queue<string> Responses { get; } = new();
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Responses.Dequeue(), Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    class StubbedGraphUtilityService : GraphUtilityService
    {
        readonly HttpClient Http;

        public StubbedGraphUtilityService(HttpMessageHandler handler)
            : base(
                Options.Create(
                    new GraphUtilityServiceOptions
                    {
                        TenantId = Guid.NewGuid().ToString(),
                        ClientId = Guid.NewGuid().ToString(),
                        ClientSecret = "secret",
                    }
                )
            )
        {
            Http = new HttpClient(handler);
        }

        public override GraphServiceClient NewAuthenticatedClient(TokenCredential credential) =>
            new(Http, new AnonymousAuthenticationProvider());
    }

    public class GraphUtilityServiceTests
    {
        readonly StubGraphHandler Handler = new();

        static string Page(string? nextLink, params object[] groups)
        {
            var body = new Dictionary<string, object> { ["value"] = groups };
            if (nextLink != null)
                body["@odata.nextLink"] = nextLink;
            return System.Text.Json.JsonSerializer.Serialize(body);
        }

        static object GroupJson(string id, string name, bool? security = true, params string[] types) =>
            new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.group",
                ["id"] = id,
                ["displayName"] = name,
                ["securityEnabled"] = security,
                ["groupTypes"] = types,
            };

        async Task<List<Group>> LookupAsync(string name) =>
            (await new StubbedGraphUtilityService(Handler).SecurityGroupsByDisplayNameAsync(name)).ToList();

        [Fact]
        public async Task FiltersByDisplayNameAndSelectsNeededFields()
        {
            Handler.Responses.Enqueue(Page(null));
            await LookupAsync("Admins");

            var query = Uri.UnescapeDataString(Handler.Requests.Single().Query);
            Assert.Contains("$filter=displayName eq 'Admins'", query);
            Assert.Contains("$select=id,displayName,securityEnabled,groupTypes", query);
            Assert.EndsWith("/groups", Handler.Requests.Single().AbsolutePath);
        }

        [Fact]
        public async Task EscapesQuotesInName()
        {
            Handler.Responses.Enqueue(Page(null));
            await LookupAsync("O'Brien's Team");

            Assert.Contains(
                "displayName eq 'O''Brien''s Team'",
                Uri.UnescapeDataString(Handler.Requests.Single().Query)
            );
        }

        [Fact]
        public async Task ReturnsOnlySecurityGroups()
        {
            var security = Guid.NewGuid().ToString();
            Handler.Responses.Enqueue(
                Page(
                    null,
                    GroupJson(security, "Admins"),
                    GroupJson(Guid.NewGuid().ToString(), "Admins", true, "Unified"),
                    GroupJson(Guid.NewGuid().ToString(), "Admins", false),
                    GroupJson(Guid.NewGuid().ToString(), "Admins", null),
                    GroupJson(Guid.NewGuid().ToString(), "Admins", true, "DynamicMembership", "unified")
                )
            );

            var groups = await LookupAsync("Admins");
            Assert.Equal(security, Assert.Single(groups).Id);
        }

        [Fact]
        public async Task DynamicSecurityGroupCounts()
        {
            Handler.Responses.Enqueue(
                Page(null, GroupJson(Guid.NewGuid().ToString(), "Admins", true, "DynamicMembership"))
            );
            Assert.Single(await LookupAsync("Admins"));
        }

        [Fact]
        public async Task FollowsEveryPage()
        {
            var a = Guid.NewGuid().ToString();
            var b = Guid.NewGuid().ToString();
            Handler.Responses.Enqueue(
                Page("https://graph.microsoft.com/v1.0/groups?$skiptoken=next", GroupJson(a, "Admins"))
            );
            Handler.Responses.Enqueue(Page(null, GroupJson(b, "Admins")));

            var groups = await LookupAsync("Admins");

            Assert.Equal(new[] { a, b }, groups.Select(x => x.Id));
            Assert.Equal(2, Handler.Requests.Count);
        }

        [Fact]
        public async Task NoMatchReturnsEmpty()
        {
            Handler.Responses.Enqueue(Page(null));
            Assert.Empty(await LookupAsync("Admins"));
        }

        [Fact]
        public async Task DuplicatesFromGraphFailDiscovery()
        {
            Handler.Responses.Enqueue(
                Page(
                    null,
                    GroupJson(Guid.NewGuid().ToString(), "Admins"),
                    GroupJson(Guid.NewGuid().ToString(), "Admins")
                )
            );
            var ex = await Assert.ThrowsAsync<GroupRoleMapException>(() =>
                Make.Map(new StubbedGraphUtilityService(Handler), null, null, "Admins").GetAsync()
            );
            Assert.Contains("2 security groups", ex.Message);
        }

        [Fact]
        public async Task SecurityGroupAndSameNamedM365GroupIsNotADuplicate()
        {
            var security = Guid.NewGuid();
            Handler.Responses.Enqueue(
                Page(
                    null,
                    GroupJson(security.ToString(), "Admins"),
                    GroupJson(Guid.NewGuid().ToString(), "Admins", true, "Unified")
                )
            );
            var map = await Make.Map(new StubbedGraphUtilityService(Handler), null, null, "Admins").GetAsync();
            Assert.Equal("Admins", map[security]);
        }

        class MinimalGraph : IGraphUtilityService { }

        [Fact]
        public async Task ImplementationWithoutLookupReportsNotSupported()
        {
            IGraphUtilityService graph = new MinimalGraph();
            var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
                graph.SecurityGroupsByDisplayNameAsync("Admins")
            );
            Assert.Contains(nameof(MinimalGraph), ex.Message);
        }
    }
}
