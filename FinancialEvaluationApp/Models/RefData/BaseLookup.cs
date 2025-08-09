using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.RefData
{
    public abstract class BaseLookup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(50)]
        public string Code { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 0;
    }
}
