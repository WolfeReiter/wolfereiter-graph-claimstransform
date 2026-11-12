using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApp_OpenIDConnect_Group_Role_Transform.Models;
using WolfeReiter.Identity.Claims;

namespace WebApp_OpenIDConnect_Group_Role_Transform.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly GroupRoleMap RoleMap;

        public HomeController(GroupRoleMap roleMap)
        {
            RoleMap = roleMap;
        }

        /// <summary>Every mapped role, whether the user holds it, and the user's claims.</summary>
        public async Task<IActionResult> Index()
        {
            var map = await RoleMap.GetAsync();
            var roles = map.GroupBy(x => x.Value)
                .OrderBy(x => x.Key)
                .Select(x => new RoleViewModel
                {
                    Role = x.Key,
                    ObjectIds = x.Select(y => y.Key.ToString()).OrderBy(y => y).ToList(),
                    InRole = User.IsInRole(x.Key),
                })
                .ToList();
            return View(roles);
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
