using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Serialization;

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
        protected async Task<IEnumerable<TResult>?> AllPagesAsync<
            TSource,
            TCollectionPage,
            TResult
        >(IBaseClient graphClient, TCollectionPage? page)
            where TResult : class
            where TCollectionPage : IParsable, IAdditionalDataHolder, new()
        {
            if (page == null)
                return null;

            var allItems = new List<TResult>();

            var pageIterator = PageIterator<TSource, TCollectionPage>.CreatePageIterator(
                graphClient,
                page,
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

        protected TokenCredential NewClientSecretCredential()
        {
            var credential = new ClientSecretCredential(
                Options.TenantId,
                Options.ClientId,
                Options.ClientSecret,
                new TokenCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzurePublicCloud }
            );
            return credential;
        }

        public GraphServiceClient NewAuthenticatedClient(TokenCredential credential)
        {
            return new GraphServiceClient(credential);
        }
    }
}
