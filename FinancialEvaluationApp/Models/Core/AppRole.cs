using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class AppRole : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public bool IsSystem { get; set; } // نقش سیستمی که کاربر نمی‌تواند حذف یا تغییر نام دهد
        public bool IsActive { get; set; } // فعال یا غیرفعال بودن نقش

        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();

    }
}
