using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Tender : BaseEntity
    {
        [Required, MaxLength(150)]
        public string Code { get; set; }  // کد مناقصه

        [Required, MaxLength(300)]
        public string Title { get; set; }

        public TenderStatus Status { get; set; } = TenderStatus.Draft;

        [Required]
        public Guid CompanyId { get; set; }
        public Company Company { get; set; }

        public DateTimeOffset? PublishDate { get; set; }
        public DateTimeOffset? ClosingDate { get; set; }

        public decimal? BaseEstimateAmount { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
    }
}
