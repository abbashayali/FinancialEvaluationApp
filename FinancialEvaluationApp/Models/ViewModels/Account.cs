using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.ViewModels
{
    public class LoginVm
    {
        [Required, StringLength(128)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(128)]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public class ChangePasswordVm
    {
        [Required, StringLength(128)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(128), MinLength(12)]
        public string NewPassword { get; set; } = string.Empty;

        [Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
