using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Bidder : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }=string.Empty;

        [MaxLength(50)]
        public string NationalId { get; set; }=string.Empty ;

        public Guid? CompanyId { get; set; }
        public Company? Company { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
    }
}
