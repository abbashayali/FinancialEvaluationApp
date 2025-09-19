using System.Collections.Generic;

namespace UserAdminKit.Abstractions;

public sealed class UserIndexVm
{
    public List<UserListItemVm> Items { get; set; } = new();
    public string Sort { get; set; } = "username"; // username | fullname | role | status | lastlogin
    public string Dir { get; set; } = "asc";       // asc | desc
}
