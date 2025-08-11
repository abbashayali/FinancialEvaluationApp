using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.RefData
{
    public class FxRate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BaseCurrencyId { get; set; }
        public Currency BaseCurrency { get; set; } = null!;

        public Guid QuoteCurrencyId { get; set; }
        public Currency QuoteCurrency { get; set; } = null!;

        public DateTimeOffset RateDate { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal Rate { get; set; }

        public string Source { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
