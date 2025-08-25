using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.ViewModels.Accounts
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "رمز فعلی را وارد کنید")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز فعلی")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز جدید را وارد کنید")]
        [MinLength(12, ErrorMessage = "حداقل ۱۲ کاراکتر")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز جدید")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تکرار رمز جدید را وارد کنید")]
        [DataType(DataType.Password)]
        [Display(Name = "تکرار رمز جدید")]
        [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
