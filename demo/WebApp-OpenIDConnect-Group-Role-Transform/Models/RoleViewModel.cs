using System.Collections.Generic;

namespace WebApp_OpenIDConnect_Group_Role_Transform.Models
{
    public class RoleViewModel
    {
        public string Role { get; set; } = string.Empty;
        public IReadOnlyList<string> ObjectIds { get; set; } = new List<string>();
        public bool InRole { get; set; }
    }
}
