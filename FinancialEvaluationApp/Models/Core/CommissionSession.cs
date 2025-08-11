using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class CommissionSession : BaseEntity
    {
        public DateTime SessionDate { get; set; }
        public string Description { get; set; } = string.Empty;

        // ارتباط با مناقصات
        public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    }
}
