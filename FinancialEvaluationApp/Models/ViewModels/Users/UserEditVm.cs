using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.ViewModels.Users
{
    public class UserEditVm
    {
        [Required]
        public Guid Id { get; set; }

        [Display(Name = "نام کاربری")]
        public string Username { get; set; } = ""; // فقط نمایش، ویرایش نمی‌کنیم

        [Required, StringLength(100)]
        [Display(Name = "نام کامل")]
        public string FullName { get; set; } = "";

        [Required]
        [Display(Name = "نقش")]
        public Guid? RoleId { get; set; }

        [Display(Name = "فعال")]
        public bool IsActive { get; set; }

        [Display(Name = "الزام تغییر گذرواژه در اولین ورود")]
        public bool MustChangePassword { get; set; }
    }
}
