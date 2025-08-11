using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class FiscalYear : BaseEntity
    {
        public string Name { get; set; } = string.Empty; // مثال: 1403-1404
        public DateTime StartDate { get; set; } // تاریخ شروع (میلادی، نمایش شمسی)
        public DateTime EndDate { get; set; }   // تاریخ پایان
        public bool IsActive { get; set; }
    }
}
