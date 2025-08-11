using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public abstract class BaseEntity
    {
        public Guid Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; } // مقدارش را DB می‌گذارد
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
    }

}

