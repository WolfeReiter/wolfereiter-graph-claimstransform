using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace WolfeReiter.Identity.Claims
{
    public interface IGraphUtilityService
    {
        Task<IEnumerable<Group>> GroupsAsync(ClaimsPrincipal principal, string accessToken);
    }
}