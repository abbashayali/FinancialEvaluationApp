using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace UserAdminKit.Abstractions
{
    public class UserEditVm
    {
        [Required]
        public Guid Id { get; set; }

        [Required, StringLength(50)]
        public string Username { get; set; } = "";

        [Required, StringLength(100)]
        public string FullName { get; set; } = "";

        public Guid? RoleId { get; set; }
        public bool IsActive { get; set; } = true;
        public bool MustChangePassword { get; set; } = false;

        public IEnumerable<SelectListItem> Roles { get; set; } = Array.Empty<SelectListItem>();
        public IEnumerable<RoleOptionVm>? RoleOptions { get; set; } = Array.Empty<RoleOptionVm>();
    }
}
