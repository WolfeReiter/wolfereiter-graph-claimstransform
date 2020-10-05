
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
        public async Task<IActionResult> SignOut(string scheme)
        {
            await Cache.RemoveGroupClaimsAsync(User);
            scheme ??= OpenIdConnectDefaults.AuthenticationScheme;
            var callbackUrl = Url.ActionLink("SignedOut");
            return SignOut(
                 new AuthenticationProperties
                 {
                     RedirectUri = callbackUrl,
                 },
                 CookieAuthenticationDefaults.AuthenticationScheme,
                 scheme);
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