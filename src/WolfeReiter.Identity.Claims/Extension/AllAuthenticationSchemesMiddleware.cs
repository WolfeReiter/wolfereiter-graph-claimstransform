using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
namespace Microsoft.AspNetCore.Authentication
{
    public class AllAuthenticationSchemesMiddleware
    {
        private readonly RequestDelegate _next;

        public AllAuthenticationSchemesMiddleware(RequestDelegate next, IAuthenticationSchemeProvider schemes)
        {      
            _next = next;
            Schemes = schemes;
        }

        public IAuthenticationSchemeProvider Schemes { get; set; }

        public async Task Invoke(HttpContext context)
        {
            context.Features.Set<IAuthenticationFeature>(new AuthenticationFeature
            {
                OriginalPath = context.Request.Path,
                OriginalPathBase = context.Request.PathBase
            });

            // Give any IAuthenticationRequestHandler schemes a chance to handle the request
            var handlers = context.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();

            //see https://github.com/dotnet/aspnetcore/blob/main/src/Security/Authentication/Core/src/AuthenticationMiddleware.cs
            //the built-in AuthenticationMiddleware will only check the scheme set in DefaultAuthenticateScheme.
            //instead, check the whole list of schemes for an authenticated user.
            foreach (var scheme in await Schemes.GetAllSchemesAsync())
            {
                if (await handlers.GetHandlerAsync(context, scheme.Name) is IAuthenticationRequestHandler handler && await handler.HandleRequestAsync())
                {
                    return;
                }
                var result = await context.AuthenticateAsync(scheme.Name);
                if (context != null)
                {
                    context.User = result.Principal;
                }
            }
            await _next(context);
        }
    }
}
