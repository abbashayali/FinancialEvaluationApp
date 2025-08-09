using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public abstract class BaseEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
    }
}
