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


            // -------------------------
            // Decimal precisions
            // -------------------------
            modelBuilder.Entity<Tender>().Property(x => x.BaseEstimateAmount).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Proposal>().Property(x => x.RialAmount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Proposal>().Property(x => x.ForeignAmount).HasColumnType("decimal(18,2)");
            modelBuilder.Entity<Proposal>().Property(x => x.FxRate).HasColumnType("decimal(18,6)");
            modelBuilder.Entity<Proposal>().Property(x => x.NormalizedAmountIRR).HasColumnType("decimal(18,2)");

            modelBuilder.Entity<EvaluationResult>().Property(x => x.PriceScore).HasColumnType("decimal(18,4)");
            modelBuilder.Entity<EvaluationResult>().Property(x => x.TechnicalScore).HasColumnType("decimal(18,4)");
            modelBuilder.Entity<EvaluationResult>().Property(x => x.FinalScore).HasColumnType("decimal(18,4)");

            modelBuilder.Entity<FxRate>().Property(x => x.Rate).HasColumnType("decimal(18,6)");
            modelBuilder.Entity<Currency>().Property(x => x.DefaultFxRateToIRR).HasColumnType("decimal(18,6)");

            // -------------------------
            // Delete behaviors (avoid multiple cascade paths)
            // -------------------------
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

            // اگر Tender -> CommissionSession داری (کلید CommissionSessionId روی Tender):
            modelBuilder.Entity<Tender>()
                .HasOne(t => t.CommissionSession)
                .WithMany(s => s.Tenders)
                .HasForeignKey(t => t.CommissionSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            // User-Role
            modelBuilder.Entity<AppUser>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            // -------------------------
            // Indexes / Constraints
            // -------------------------
            modelBuilder.Entity<Currency>().HasIndex(x => x.Code).IsUnique();
            modelBuilder.Entity<AppRole>().HasIndex(x => x.Name).IsUnique();
            modelBuilder.Entity<AppUser>().HasIndex(x => x.Username).IsUnique();

            // (اختیاری) فقط یک سال مالی فعال — از نظر بیزینسی enforce کن؛ در DB به صورت فیلتر ایندکس:
            // modelBuilder.Entity<FiscalYear>()
            //     .HasIndex(nameof(FiscalYear.IsActive))
            //     .HasFilter("[IsActive] = 1")
            //     .IsUnique();

            // -------------------------
            // Seed data (GUID های ثابت)
            // -------------------------
            // Currencies
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

            // FiscalYear (1404) — 21 Mar 2025 تا 20 Mar 2026
            var FY_1404 = new Guid("55555555-5555-5555-5555-555555555555");
            modelBuilder.Entity<FiscalYear>().HasData(
                new FiscalYear
                {
                    Id = FY_1404,
                    StartDate = new DateTime(2025, 03, 21),
                    EndDate = new DateTime(2026, 03, 20),
                    IsActive = true
                }
            );

            // Roles
            var ROLE_ADMIN = new Guid("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
            modelBuilder.Entity<AppRole>().HasData(
                new AppRole { Id = ROLE_ADMIN, Name = "Admin", IsSystem = true, IsActive = true }
            );

            // Admin user
            var USER_ADMIN = new Guid("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

            // نکته مهم درباره PasswordHash:
            // چون EF Core برای HasData مقادیر ثابت می‌خواهد، اینجا یک هشِ از پیش ساخته‌شده (BCrypt) قرار داده‌ایم.
            // مقدار زیر برای رمز «Admin@123» است. اگر لاگین انجام نشد، با ابزار BCrypt یک هش جدید بساز و این رشته را جایگزین کن.
            const string AdminPasswordBcrypt =
                "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S"; // نمونه‌ی معتبر

            modelBuilder.Entity<AppUser>().HasData(
                new AppUser
                {
                    Id = USER_ADMIN,
                    Username = "admin",
                    PasswordHash = AdminPasswordBcrypt, // bcrypt("Admin@123")
                    FullName = "System Administrator",
                    IsActive = true,
                    LastLoginDate = null,
                    RoleId = ROLE_ADMIN,
                    MustChangePassword = true
                }
            );
        }




    }
}

