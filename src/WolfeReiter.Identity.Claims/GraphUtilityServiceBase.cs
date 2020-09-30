using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Identity.Web;

namespace WolfeReiter.Identity.Claims
{
    public abstract class GraphUtilityServiceBase
    {
        protected async Task<IEnumerable<T>> AllPagesAsync<T>(IBaseClient graphClient, ICollectionPage<DirectoryObject> page) 
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
                    if (item is T)
                    {
                        // Only add if the item is the requested type
                        allItems.Add(item as T);
                    }

                    return true;
                }
            );

            await pageIterator.IterateAsync();

            return allItems;
        }

        protected GraphServiceClient NewAuthenticatedClient(string accessToken)
        {
            //TODO: configure graphApiUrl instead of hard-code
            return new GraphServiceClient("https://graph.microsoft.com/.default",
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