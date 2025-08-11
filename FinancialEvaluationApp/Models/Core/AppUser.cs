using System;

namespace FinancialEvaluationApp.Models.Core
{
    public class AppUser : BaseEntity
    {
        public string Username { get; set; }=string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;    
        public bool IsActive { get; set; }=true;
        public DateTimeOffset? LastLoginDate { get; set; }

        public bool MustChangePassword { get; set; } = false;

        public Guid? RoleId { get; set; }
        public AppRole Role { get; set; } = null!;
    }
}
