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
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<AppRole> AppRoles { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // AppUser
            modelBuilder.Entity<AppUser>(e =>
            {
                e.Property(x => x.Username)
                    .IsRequired()
                    .HasMaxLength(50);           // هم‌راستا با VM
                e.Property(x => x.FullName)
                    .IsRequired()
                    .HasMaxLength(100);

                e.HasIndex(x => x.Username).IsUnique();   // فقط همین یک بار

                // اختیاری: EF خودش برای DateTimeOffset همین نوع را می‌گذارد،
                // ولی اگر می‌خواهی صریح باشد، این خط خوب است:
                e.Property(x => x.LastLoginDate)
                    .HasColumnType("datetimeoffset");
            });

            // AppRole
            modelBuilder.Entity<AppRole>(e =>
            {
                e.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(50);
                e.HasIndex(x => x.Name).IsUnique();
            });

            modelBuilder.Entity<Currency>(e =>
            {
                e.Property(x => x.Code).IsRequired().HasMaxLength(10);
                e.HasIndex(x => x.Code).IsUnique();      // این خط باعث می‌شود EF ایندکس را بسازد
            });

            // Tender
            modelBuilder.Entity<Tender>(e =>
            {
                // مبلغ‌ها را با دقت امن ذخیره کن (SQL Server: decimal(18,2))
                e.Property(x => x.BaseEstimateAmount).HasPrecision(18, 2);
                // اگر مبلغ‌های دیگری داری، همین‌جا اضافه کن:
                // e.Property(x => x.SomeOtherAmount).HasPrecision(18, 2);
            });

            // اگر نرخ ارز داری (مثلاً FxRate.Rate) دقت بیشتری بده:
            modelBuilder.Entity<FxRate>(e =>
            {
                e.Property(x => x.Rate).HasPrecision(18, 6);
            });


            // مقدار پیش‌فرض فیلدهای BaseEntity را DB بده، نه کد
            foreach (var et in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(et.ClrType))
                {
                    var eb = modelBuilder.Entity(et.ClrType);
                    eb.Property<DateTimeOffset>(nameof(BaseEntity.CreatedAt))
                      .HasDefaultValueSql("SYSUTCDATETIME()");
                    eb.Property<bool>(nameof(BaseEntity.IsDeleted))
                      .HasDefaultValue(false);
                    // UpdatedAt اختیاری است؛ پیش‌فرض لازم ندارد
                }
            }
           

        }





    }
}

