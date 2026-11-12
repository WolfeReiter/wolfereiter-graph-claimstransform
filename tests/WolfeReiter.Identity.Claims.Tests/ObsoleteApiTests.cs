using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace WolfeReiter.Identity.Claims.Tests
{
    /// <summary>
    /// The per user cache and the sign in membership query are retired. They are obsolete as
    /// compile errors, so they are reached here only through reflection.
    /// </summary>
    public class ObsoleteApiTests
    {
        static readonly Assembly Library = typeof(GroupRoleMap).Assembly;

        static Type CacheExtension =>
            Library.GetType("Microsoft.Extensions.Caching.Distributed.GroupClaimsDistributedExtension", true)!;

        static void AssertObsoleteError(MemberInfo member)
        {
            var attribute = member.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(attribute);
            Assert.True(attribute!.IsError, $"{member.Name} must be obsolete as an error.");
        }

        public static TheoryData<string> CacheMethods =>
            new() { "SetGroupClaimsAsync", "GetGroupClaimsAsync", "RemoveGroupClaimsAsync" };

        [Theory]
        [MemberData(nameof(CacheMethods))]
        public void CacheExtensionMethodIsObsoleteErrorAndThrows(string name)
        {
            AssertObsoleteError(CacheExtension);
            var method = CacheExtension.GetMethod(name)!;
            AssertObsoleteError(method);

            var args = method.GetParameters().Select(_ => (object?)null).ToArray();
            var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, args));
            Assert.IsType<NotSupportedException>(ex.InnerException);
        }

        [Fact]
        public void AsyncResultIsObsoleteError() =>
            AssertObsoleteError(Library.GetType("WolfeReiter.Threading.Tasks.AsyncResult`1", true)!);

        [Theory]
        [InlineData("GroupClaimsCacheAbsoluteExpirationHours")]
        [InlineData("GroupClaimsCacheSlidingExpirationMinutes")]
        [InlineData("DistributedCacheEntryOptions")]
        public void CacheOptionIsObsoleteError(string name) =>
            AssertObsoleteError(typeof(GraphUtilityServiceOptions).GetProperty(name)!);

        [Fact]
        public void CacheEntryOptionsThrows()
        {
            var property = typeof(GraphUtilityServiceOptions).GetProperty("DistributedCacheEntryOptions")!;
            var ex = Assert.Throws<TargetInvocationException>(() =>
                property.GetValue(new GraphUtilityServiceOptions())
            );
            Assert.IsType<NotSupportedException>(ex.InnerException);
        }

        [Fact]
        public async Task MembershipQueryIsObsoleteErrorAndThrows()
        {
            var method = typeof(IGraphUtilityService).GetMethod("GroupsAsync")!;
            AssertObsoleteError(method);
            Assert.Null(typeof(GraphUtilityService).GetMethod("GroupsAsync", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

            var ex = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(new FakeGraph(), new object[] { new ClaimsPrincipal() })
            );
            Assert.IsType<NotSupportedException>(ex.InnerException);
            await Task.CompletedTask;
        }
    }
}
