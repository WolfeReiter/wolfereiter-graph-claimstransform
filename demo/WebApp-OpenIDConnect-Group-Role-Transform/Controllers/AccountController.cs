
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace WebApp_OpenIDConnect_Group_Role_Transform.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly IDistributedCache Cache;
        public AccountController(IDistributedCache cache)
        {
            Cache = cache;
        }

        /// <summary>
        /// Replacement for Microsoft.Identity.Web.UI Account/Signout that clears groups from cache for User
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
            await Cache.RemoveGroupClaimsAsync(User);
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