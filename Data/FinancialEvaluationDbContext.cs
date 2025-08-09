using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Models.Core;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Data
{
    public class FinancialEvaluationDbContext : DbContext
    {
        public FinancialEvaluationDbContext(DbContextOptions<FinancialEvaluationDbContext> options) : base(options) { }

        public DbSet<Company> Companies { get; set; }
        public DbSet<Tender> Tenders { get; set; }
        public DbSet<Bidder> Bidders { get; set; }
        public DbSet<Proposal> Proposals { get; set; }
        public DbSet<EvaluationResult> EvaluationResults { get; set; }

        public DbSet<Currency> Currencies { get; set; }
        public DbSet<FxRate> FxRates { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // «Ì‰ »Œ‘ —« œ— „—Õ·Â ? »—«Ì —Ê«»ÿ/«Ì‰œò”/Seed ò«„· „Ìùò‰Ì„.
        }
    }
}
