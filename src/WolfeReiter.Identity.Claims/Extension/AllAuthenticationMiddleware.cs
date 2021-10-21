using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features.Authentication;

namespace WolfeReiter.AspNetCore.Authentication
{
    public class AllAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;

        public AllAuthenticationMiddleware(RequestDelegate next, IAuthenticationSchemeProvider schemes)
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
