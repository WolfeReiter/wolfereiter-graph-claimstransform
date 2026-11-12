using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// Builds the <see cref="GroupRoleMap"/> before the app serves requests, so a missing or
    /// duplicated group name, or a missing mapping, stops startup instead of failing at sign in.
    /// </summary>
    public class GroupRoleMapStartup : IHostedService
    {
        private readonly GroupRoleMap Map;
        private readonly ILogger Logger;

        public GroupRoleMapStartup(GroupRoleMap map, ILogger<GroupRoleMapStartup> logger)
        {
            Map = map;
            Logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Map.GetAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Logger.LogCritical(e, "Group role map failed. {Message}", e.Message);
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
