using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.RefData
{
    public class Currency : BaseLookup
    {
        public string Symbol { get; set; }
        [Column(TypeName = ""decimal(18,6)"")]
        public decimal? DefaultFxRateToIRR { get; set; }
        public bool IsDefault { get; set; } = false;
    }
}
