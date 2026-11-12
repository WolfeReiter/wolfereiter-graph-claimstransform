using System;

namespace WolfeReiter.Identity.Claims
{
    /// <summary>
    /// The group to role map cannot be built, for example because a configured group name matches
    /// no security group or more than one.
    /// </summary>
    public class GroupRoleMapException : Exception
    {
        public GroupRoleMapException(string message)
            : base(message) { }

        public GroupRoleMapException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
