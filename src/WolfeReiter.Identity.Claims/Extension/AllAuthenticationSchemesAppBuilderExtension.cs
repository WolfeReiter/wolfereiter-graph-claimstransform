using Microsoft.AspNetCore.Authentication;

namespace Microsoft.AspNetCore.Builder
{
    /// <summary>
    /// Extension methods to add authentication capabilities to an HTTP application pipeline.
    /// </summary>
    public static class AllAuthenticationSchemesBuilderExtension
    {
        /// <summary>
        /// Adds the <see cref="AllAuthenticationMiddleware"/> to the specified 
        /// <see cref="IApplicationBuilder"/>, which enables authentication capabilities by checking all registered
        /// schemes.
        /// </summary>
        /// <param name="app">The <see cref="IApplicationBuilder"/> to add the middleware to.</param>
        /// <returns>A reference to this instance after the operation has completed.</returns>
        public static IApplicationBuilder UseAllAuthenticationSchemes(this IApplicationBuilder app)
        {
            return app.UseMiddleware<AllAuthenticationSchemesMiddleware>();
        }
    }
}