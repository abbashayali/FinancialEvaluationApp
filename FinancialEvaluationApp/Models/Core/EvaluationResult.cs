using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.Core
{
    public class EvaluationResult : BaseEntity
    {
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; }

        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal PriceScore { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal TechnicalScore { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal FinalScore { get; set; }

        public int? Rank { get; set; }
        public string Notes { get; set; }
    }
}
