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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="graphClient">Authenticated GraphServiceClient object.</param>
        /// <param name="page">Result page object from .GetAsync() method</param>
        /// <typeparam name="TSource">Source collection member type (e.g. DirectoryObject, User, Group)</typeparam>
        /// <typeparam name="TResult">Result collection member type (e.g. Group, User)</typeparam>
        /// <returns></returns>
        protected async Task<IEnumerable<TResult>?> AllPagesAsync<TSource, TResult>(
            IBaseClient graphClient, ICollectionPage<TSource> page) 
            where TResult : class
        {
            if (page == null) return null;

            var allItems = new List<TResult>();

            var pageIterator = PageIterator<TSource>.CreatePageIterator(
                graphClient, page,
                (item) =>
                {
                    // This code executes for each item in the
                    // collection
                    if (item is TResult t)
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