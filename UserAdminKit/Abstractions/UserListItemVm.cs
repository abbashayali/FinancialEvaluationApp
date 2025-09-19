using System;

namespace UserAdminKit.Abstractions
{
    public sealed class UserListItemVm
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public DateTime? LastLogin { get; set; }
        public string LastLoginDisplay => LastLogin.HasValue
            ? LastLogin.Value.ToString("yyyy-MM-dd HH:mm")
            : "—";
    }
}
