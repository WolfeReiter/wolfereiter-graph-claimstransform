
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace WebApp_OpenIDConnect_Group_Role_Transform.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        /// <summary>
        /// Replacement for Microsoft.Identity.Web.UI Account/Signout that signs out of both the cookie and OpenID Connect schemes
        /// </summary>
        /// <param name="scheme"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("/Account/SignOut/")]
#if NET6_0_OR_GREATER // net6.0 ControllerBase added SignOut() but it is not async so it has a different return type, using new to replace it.
        public new async Task<IActionResult> SignOut()
#else
        public async Task<IActionResult> SignOut()
#endif
        {
            await Task.CompletedTask;
            return SignOut(
                 new AuthenticationProperties
                 {
                    RedirectUri = Url.ActionLink("SignedOut")
                 },
                 CookieAuthenticationDefaults.AuthenticationScheme,
                 OpenIdConnectDefaults.AuthenticationScheme
                 );
        }

        [HttpGet]
        public IActionResult SignedOut()
        {
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                return LocalRedirect("~/");
            }

            return View();
        }
    }
}