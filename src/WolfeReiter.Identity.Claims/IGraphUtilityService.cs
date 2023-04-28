using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Graph.Models;

namespace WolfeReiter.Identity.Claims
{
    public interface IGraphUtilityService
    {
        Task<IEnumerable<Group>?> GroupsAsync(ClaimsPrincipal principal);
    }
}
