using System;

namespace FinancialEvaluationApp.ViewModels.Users
{
    public class ResetPasswordResultVm
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleName { get; set; } = "";
        public string TemporaryPassword { get; set; } = "";
    }
}
