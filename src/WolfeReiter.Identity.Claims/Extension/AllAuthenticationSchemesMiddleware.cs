using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
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
            foreach (var scheme in await Schemes.GetRequestHandlerSchemesAsync())
            {
                if (await handlers.GetHandlerAsync(context, scheme.Name) is IAuthenticationRequestHandler handler && await handler.HandleRequestAsync())
                {
                    return;
                }
            }
            //see https://github.com/dotnet/aspnetcore/blob/v3.1.20/src/Security/Authentication/Core/src/AuthenticationMiddleware.cs
            //the built-in AuthenticationMiddleware will only check the scheme set in DefaultAuthenticateScheme.
            //instead, check the whole list of schemes for an authenticated user.

            //check the default authentication scheme first and then continue to the rest.
            var defaultAuthenticate = await Schemes.GetDefaultAuthenticateSchemeAsync();
            var schemes = new List<AuthenticationScheme>();
            if (defaultAuthenticate != null)
            {
                schemes.Add(defaultAuthenticate);
                schemes.AddRange((await Schemes.GetAllSchemesAsync()).Where(x => x != defaultAuthenticate));
            }
            else
            {
                schemes.AddRange(await Schemes.GetAllSchemesAsync());
            }
            foreach (var scheme in schemes)
            {
                var result = await context.AuthenticateAsync(scheme.Name);
                if (result?.Principal != null)
                {
                    context.User = result.Principal;
                    break;
                }
            }

            await _next(context);
        }
    }
}
