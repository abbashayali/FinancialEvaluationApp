using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Company : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }

        [MaxLength(50)]
        public string RegistrationNo { get; set; }

        public Guid? ParentCompanyId { get; set; }
        public Company ParentCompany { get; set; }

        public ICollection<Company> Subsidiaries { get; set; } = new List<Company>();
        public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
        public ICollection<Bidder> Bidders { get; set; } = new List<Bidder>();
    }
}
