using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Models.RefData;
using FinancialEvaluationApp.Models.Core;

namespace FinancialEvaluationApp.Data
{
    public class FinancialEvaluationDbContext : DbContext
    {
        public FinancialEvaluationDbContext(DbContextOptions<FinancialEvaluationDbContext> options)
            : base(options) { }

        // Core
        public DbSet<Company> Companies { get; set; }
        public DbSet<Tender> Tenders { get; set; }
        public DbSet<Bidder> Bidders { get; set; }
        public DbSet<Proposal> Proposals { get; set; }
        public DbSet<EvaluationResult> EvaluationResults { get; set; }

        // RefData
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<FxRate> FxRates { get; set; }




        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- Fix decimal precisions ---
            modelBuilder.Entity<Tender>()
                .Property(x => x.BaseEstimateAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Proposal>().Property(x => x.RialAmount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Proposal>().Property(x => x.ForeignAmount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Proposal>().Property(x => x.FxRate).HasColumnType("decimal(18,6)");
            modelBuilder.Entity<Proposal>().Property(x => x.NormalizedAmountIRR).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<EvaluationResult>().Property(x => x.PriceScore).HasColumnType("decimal(18,4)");
            modelBuilder.Entity<EvaluationResult>().Property(x => x.TechnicalScore).HasColumnType("decimal(18,4)");
            modelBuilder.Entity<EvaluationResult>().Property(x => x.FinalScore).HasColumnType("decimal(18,4)");

            modelBuilder.Entity<FxRate>().Property(x => x.Rate).HasColumnType("decimal(18,6)");
            modelBuilder.Entity<Currency>().Property(x => x.DefaultFxRateToIRR).HasColumnType("decimal(18,6)");

            // --- Delete behaviors to avoid multiple cascade paths ---
            modelBuilder.Entity<FxRate>(e =>
            {
                e.HasOne(x => x.BaseCurrency).WithMany()
                    .HasForeignKey(x => x.BaseCurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(x => x.QuoteCurrency).WithMany()
                    .HasForeignKey(x => x.QuoteCurrencyId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Proposal>()
                .HasOne(x => x.ForeignCurrency).WithMany()
                .HasForeignKey(x => x.ForeignCurrencyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Company>()
                .HasOne(x => x.ParentCompany)
                .WithMany(x => x.Subsidiaries)
                .HasForeignKey(x => x.ParentCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // --- Seed initial currencies (GUIDهای ثابت) ---
            var IRR = new Guid("11111111-1111-1111-1111-111111111111");
            var USD = new Guid("22222222-2222-2222-2222-222222222222");
            var EUR = new Guid("33333333-3333-3333-3333-333333333333");
            var AED = new Guid("44444444-4444-4444-4444-444444444444");

            modelBuilder.Entity<Currency>().HasData(
                new Currency { Id = IRR, Code = "IRR", Name = "Iranian Rial", Symbol = "﷼", IsDefault = true, IsActive = true, SortOrder = 1 },
                new Currency { Id = USD, Code = "USD", Name = "US Dollar", Symbol = "$", IsDefault = false, IsActive = true, SortOrder = 2 },
                new Currency { Id = EUR, Code = "EUR", Name = "Euro", Symbol = "€", IsDefault = false, IsActive = true, SortOrder = 3 },
                new Currency { Id = AED, Code = "AED", Name = "UAE Dirham", Symbol = "AED", IsDefault = false, IsActive = true, SortOrder = 4 }
            );
        }



    }
}

