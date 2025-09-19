using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace UserAdminKit.Abstractions
{
    public class UserCreateVm
    {
        public Guid? Id { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; } = "";

        [Required, StringLength(100)]
        public string FullName { get; set; } = "";

        public Guid? RoleId { get; set; }
        public bool IsActive { get; set; } = true;

        [Required, StringLength(100, MinimumLength = 12)]
        [DataType(DataType.Password)]
        public string Password
        {
            get => TemporaryPassword;
            set => TemporaryPassword = value;
        }

        [StringLength(100, MinimumLength = 12)]
        [DataType(DataType.Password)]
        public string TemporaryPassword { get; set; } = "";

        public bool MustChangePassword { get; set; } = true;

        // برای asp-items در ویو
        public IEnumerable<SelectListItem> Roles { get; set; } = Array.Empty<SelectListItem>();

        // برای سناریوهایی که منبع خام می‌خواهی
        public IEnumerable<RoleOptionVm>? RoleOptions { get; set; } = Array.Empty<RoleOptionVm>();
    }
}
