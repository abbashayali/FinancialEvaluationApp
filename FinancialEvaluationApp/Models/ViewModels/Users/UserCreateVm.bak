using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.ViewModels.Users
{
    public class UserCreateVm
    {
        [Required, StringLength(50)]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; } = "";

        [Required, StringLength(100)]
        [Display(Name = "نام کامل")]
        public string FullName { get; set; } = "";

        [Required]
        [Display(Name = "نقش")]
        public Guid RoleId { get; set; }

        [Display(Name = "فعال")]
        public bool IsActive { get; set; } = true;

        [Required, StringLength(100, MinimumLength = 12)]
        [DataType(DataType.Password)]
        [Display(Name = "گذرواژه موقت")]
        public string TemporaryPassword { get; set; } = "";

        [Display(Name = "الزام تغییر گذرواژه در اولین ورود")]
        public bool MustChangePassword { get; set; } = true;
    }
}
