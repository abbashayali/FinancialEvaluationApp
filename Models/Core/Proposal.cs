using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Models.Core
{
    public class Proposal : BaseEntity
    {
        [Required]
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; }

        [Required]
        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; }

        [Required]
        public ProposalType Type { get; set; }

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? RialAmount { get; set; }

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? ForeignAmount { get; set; }

        // »ÂùÃ«Ì enum° ò·Ìœ Œ«—ÃÌ »Â ÃœÊ· Currency
        public Guid? ForeignCurrencyId { get; set; }
        public Currency ForeignCurrency { get; set; }

        [Column(TypeName = ""decimal(18,6)"")]
        public decimal? FxRate { get; set; }

        [MaxLength(100)]
        public string ProposalNo { get; set; }

        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? NormalizedAmountIRR { get; set; }
    }
}
