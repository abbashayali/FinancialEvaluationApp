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
        public DbSet<FiscalYear> FiscalYears { get; set; }
        public DbSet<CommissionSession> CommissionSessions { get; set; }

        // Auth
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<AppRole> AppRoles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // --- AppUser
            modelBuilder.Entity<AppUser>(e =>
            {
                e.Property(x => x.Username)
                    .IsRequired()
                    .HasMaxLength(50);

                e.Property(x => x.FullName)
                    .IsRequired()
                    .HasMaxLength(100);

                e.HasIndex(x => x.Username).IsUnique();

                // صراحت نوع تاریخ
                e.Property(x => x.LastLoginDate).HasColumnType("datetimeoffset");
            });

            // ❗ اطلاع به EF: این جدول تریگر دارد
            modelBuilder.Entity<AppUser>().ToTable(tb =>
            {
                tb.HasTrigger("TR_AppUsers_BlockExternalRoleUpdate");
                tb.HasTrigger("TR_AppUsers_OneAdmin");
            });

            // --- AppRole
            modelBuilder.Entity<AppRole>(e =>
            {
                e.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(50);

                e.HasIndex(x => x.Name).IsUnique();
            });

            // ❗ اطلاع به EF: این جدول هم تریگر دارد
            modelBuilder.Entity<AppRole>().ToTable(tb =>
            {
                tb.HasTrigger("TR_AppRoles_BlockExternalDml");
            });

            // --- Currency
            modelBuilder.Entity<Currency>(e =>
            {
                e.Property(x => x.Code).IsRequired().HasMaxLength(10);
                e.HasIndex(x => x.Code).IsUnique();
            });

            // --- Tender
            modelBuilder.Entity<Tender>(e =>
            {
                // دقت مالی
                e.Property(x => x.BaseEstimateAmount).HasPrecision(18, 2);
            });

            // --- FxRate
            modelBuilder.Entity<FxRate>(e =>
            {
                e.Property(x => x.Rate).HasPrecision(18, 6);
            });

            // --- Defaults برای BaseEntity
            foreach (var et in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(et.ClrType))
                {
                    var eb = modelBuilder.Entity(et.ClrType);
                    eb.Property<DateTimeOffset>(nameof(BaseEntity.CreatedAt))
                      .HasDefaultValueSql("SYSUTCDATETIME()");
                    eb.Property<bool>(nameof(BaseEntity.IsDeleted))
                      .HasDefaultValue(false);
                }
            }
        }
    }
}
