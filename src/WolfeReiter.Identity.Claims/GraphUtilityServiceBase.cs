using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    public abstract class GraphUtilityServiceBase
    {
        private readonly GraphUtilityServiceOptions Options;
        public GraphUtilityServiceBase(IOptions<GraphUtilityServiceOptions> options)
        {
            Options = options.Value;
        }
        protected async Task<IEnumerable<T>?> AllPagesAsync<T>(IBaseClient graphClient, ICollectionPage<DirectoryObject> page) 
            where T : class
        {
            if (page == null) return null;

            var allItems = new List<T>();

            var pageIterator = PageIterator<DirectoryObject>.CreatePageIterator(
                graphClient, page,
                (item) =>
                {
                    // This code executes for each item in the
                    // collection
                    if (item is T t)
                    {
                        // Only add if the item is the requested type
                        allItems.Add(t);
                    }

                    return true;
                }
            );

            await pageIterator.IterateAsync();

            return allItems;
        }

        protected GraphServiceClient NewAuthenticatedClient(string accessToken)
        {
            return new GraphServiceClient(Options.GraphApiVersion,
                new DelegateAuthenticationProvider(
                    async (requestMessage) =>
                    {
                        await Task.Run(() =>
                        {
                            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
                        });
                    }));

        }
    }
}