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
            // اين بخش را در مرحله ? براي روابط/ايندکس/Seed کامل مي‌کنيم.
        }
    }
}
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC: Anti-Forgery + Authorize سراسری
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new AuthorizeFilter()); // همه صفحات نیاز به لاگین
});

// DbContext
builder.Services.AddDbContext<FinancialEvaluationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cache و سرویس‌ها
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ILookupService, LookupService>();

// نرخ‌محدودسازی لاگین
builder.Services.AddSingleton<ILoginThrottle, MemoryLoginThrottle>();

// DataProtection: کلیدها پایدار
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "dp-keys")))
    .SetApplicationName("FinancialEvaluationApp");

// احراز هویت کوکی
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "FEA.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.ReturnUrlParameter = "returnUrl";
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// هدرهای امنیتی پایه + CSP
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'self';";
    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication(); // حتماً قبل از Authorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.ViewModels;
using FinancialEvaluationApp.Models.ViewModels.Accounts;

namespace FinancialEvaluationApp.Controllers
{
    [AllowAnonymous] // پیش‌فرض: فقط اکشن‌هایی که علامت‌گذاری شده‌اند بازند (Authorize سراسری فعال است)
    public class AccountController : Controller
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly ILoginThrottle _throttle;

        public AccountController(FinancialEvaluationDbContext db, ILoginThrottle throttle)
        {
            _db = db;
            _throttle = throttle;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return Redirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);

            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (_throttle.IsLocked(vm.Username, ip, out var retry))
            {
                ModelState.AddModelError("", $"حساب موقتاً قفل شد. بعد از {(int)retry!.Value.TotalMinutes} دقیقه دوباره تلاش کنید.");
                return View(vm);
            }

            var inputUsername = (vm.Username ?? string.Empty).Trim().ToLower();
            var user = await _db.AppUsers.Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username.ToLower() == inputUsername && u.IsActive);

            if (user == null || !BCrypt.Net.BCrypt.Verify(vm.Password, user.PasswordHash))
            {
                _throttle.RegisterFail(vm.Username, ip);
                ModelState.AddModelError("", "نام کاربری یا رمز عبور اشتباه است.");
                return View(vm);
            }

            _throttle.RegisterSuccess(vm.Username, ip);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.Name),
                new Claim("username", user.Username)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            if (user.MustChangePassword)
                return RedirectToAction(nameof(ChangePassword));

            user.LastLoginDate = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            return !string.IsNullOrWhiteSpace(vm.ReturnUrl)
                ? LocalRedirect(vm.ReturnUrl!)
                : RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpGet]
        public IActionResult ChangePassword() => View(new ChangePasswordViewModel());


        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userId is null) return Unauthorized();

            var uid = Guid.Parse(userId.Value);
            var user = await _db.AppUsers.FindAsync(uid);
            if (user is null) return Unauthorized();

            // بررسی رمز فعلی
            if (!BCrypt.Net.BCrypt.Verify(vm.CurrentPassword, user.PasswordHash))
            {
                ModelState.AddModelError("", "رمز فعلی درست نیست.");
                return View(vm);
            }

            // سیاست ساده: حداقل ۱۲ کاراکتر
            if (vm.NewPassword.Length < 12)
            {
                ModelState.AddModelError("", "رمز جدید باید حداقل ۱۲ کاراکتر باشد.");
                return View(vm);
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword);
            user.MustChangePassword = false;
            user.LastLoginDate = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();

            TempData["msg"] = "رمز عبور با موفقیت تغییر کرد.";
            return RedirectToAction("Index", "Home");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login","Account");
        }

        public IActionResult AccessDenied() => View();
    }
}
using System.Diagnostics;
using FinancialEvaluationApp.Models;
using Microsoft.AspNetCore.Mvc;

namespace FinancialEvaluationApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.Core;              // AppUser, AppRole
using FinancialEvaluationApp.ViewModels.Users;         // UserListItemVm
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialEvaluationApp.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly FinancialEvaluationDbContext _db; // ← نام DbContext خودت

        public UsersController(FinancialEvaluationDbContext db)
        {
            _db = db;
        }

        // GET: /Users
        public async Task<IActionResult> Index(string? sort = "username", string? dir = "asc")
        {
            sort = (sort ?? "username").ToLowerInvariant();
            dir = (dir ?? "asc").ToLowerInvariant();

            var query = _db.AppUsers
                .Include(u => u.Role)
                .AsNoTracking();

            // سورت دینامیک
            bool asc = dir != "desc";
            query = sort switch
            {
                "fullname" => asc ? query.OrderBy(u => u.FullName)
                                   : query.OrderByDescending(u => u.FullName),

                "role" => asc ? query.OrderBy(u => u.Role != null ? u.Role.Name : "")
                                   : query.OrderByDescending(u => u.Role != null ? u.Role.Name : ""),

                "lastlogin" => asc ? query.OrderBy(u => u.LastLoginDate)
                                   : query.OrderByDescending(u => u.LastLoginDate),

                "status" => asc ? query.OrderBy(u => u.IsActive)
                                   : query.OrderByDescending(u => u.IsActive),

                _ => asc ? query.OrderBy(u => u.Username)
                                   : query.OrderByDescending(u => u.Username),
            };

            var users = await query
                .Select(u => new UserListItemVm
                {
                    Id = u.Id,
                    Username = u.Username ?? "",
                    FullName = u.FullName ?? "",
                    RoleName = u.Role != null ? u.Role.Name : "—",
                    IsActive = u.IsActive,
                    LastLoginDisplay = u.LastLoginDate.HasValue
                        ? u.LastLoginDate.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                        : null
                })
                .ToListAsync();

            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            return View(users);
        }

    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinancialEvaluationApp.Controllers.Setup
{
    public class CurrencyController : Controller
    {
        private readonly FinancialEvaluationDbContext _context;

        public CurrencyController(FinancialEvaluationDbContext context)
        {
            _context = context;
        }

        // لیست همه ارزها
        public async Task<IActionResult> Index()
        {
            var list = await _context.Currencies
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return View(list);
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinancialEvaluationApp.Controllers.Setup
{
    public class FxRateController : Controller
    {
        private readonly FinancialEvaluationDbContext _context;

        public FxRateController(FinancialEvaluationDbContext context)
        {
            _context = context;
        }

        // GET: Setup/FxRate
        public async Task<IActionResult> Index()
        {
            var list = await _context.FxRates
                .Include(r => r.BaseCurrency)
                .Include(r => r.QuoteCurrency)
                .OrderByDescending(r => r.RateDate)
                .ToListAsync();

            return View(list);
        }

        // GET: Setup/FxRate/Create
        public IActionResult Create()
        {
            ViewBag.Currencies = _context.Currencies
                .OrderBy(c => c.Name)
                .ToList();
            return View();
        }

        // POST: Setup/FxRate/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FxRate fxRate)
        {
            if (ModelState.IsValid)
            {
                fxRate.Id = Guid.NewGuid();
                _context.Add(fxRate);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Currencies = _context.Currencies
                .OrderBy(c => c.Name)
                .ToList();
            return View(fxRate);
        }
    }
}
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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegistrationNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ParentCompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Companies_ParentCompanyId",
                        column: x => x.ParentCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultFxRateToIRR = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Bidders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bidders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bidders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tenders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosingDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BaseEstimateAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenders_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FxRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaseCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RateDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_BaseCurrencyId",
                        column: x => x.BaseCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FxRates_Currencies_QuoteCurrencyId",
                        column: x => x.QuoteCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BidderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PriceScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    TechnicalScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    FinalScore = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationResults_Bidders_BidderId",
                        column: x => x.BidderId,
                        principalTable: "Bidders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationResults_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Proposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BidderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    RialAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ForeignAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ForeignCurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FxRate = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    ProposalNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NormalizedAmountIRR = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Proposals_Bidders_BidderId",
                        column: x => x.BidderId,
                        principalTable: "Bidders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Proposals_Currencies_ForeignCurrencyId",
                        column: x => x.ForeignCurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Proposals_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Currencies",
                columns: new[] { "Id", "Code", "DefaultFxRateToIRR", "IsActive", "IsDefault", "Name", "SortOrder", "Symbol" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), "IRR", null, true, true, "Iranian Rial", 1, "﷼" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), "USD", null, true, false, "US Dollar", 2, "$" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), "EUR", null, true, false, "Euro", 3, "€" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), "AED", null, true, false, "UAE Dirham", 4, "AED" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bidders_CompanyId",
                table: "Bidders",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                table: "Companies",
                column: "ParentCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_BidderId",
                table: "EvaluationResults",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_TenderId",
                table: "EvaluationResults",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_BaseCurrencyId",
                table: "FxRates",
                column: "BaseCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_FxRates_QuoteCurrencyId",
                table: "FxRates",
                column: "QuoteCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_BidderId",
                table: "Proposals",
                column: "BidderId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_ForeignCurrencyId",
                table: "Proposals",
                column: "ForeignCurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Proposals_TenderId",
                table: "Proposals",
                column: "TenderId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_CompanyId",
                table: "Tenders",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationResults");

            migrationBuilder.DropTable(
                name: "FxRates");

            migrationBuilder.DropTable(
                name: "Proposals");

            migrationBuilder.DropTable(
                name: "Bidders");

            migrationBuilder.DropTable(
                name: "Currencies");

            migrationBuilder.DropTable(
                name: "Tenders");

            migrationBuilder.DropTable(
                name: "Companies");
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250809212007_Init")]
    partial class Init
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Add_FiscalYear_CommissionSession_User_Role : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CommissionSessionId",
                table: "Tenders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommissionSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommissionSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FiscalYears",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalYears", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUsers_AppRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AppRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_RoleId",
                table: "AppUsers",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropTable(
                name: "CommissionSessions");

            migrationBuilder.DropTable(
                name: "FiscalYears");

            migrationBuilder.DropTable(
                name: "AppRoles");

            migrationBuilder.DropIndex(
                name: "IX_Tenders_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.DropColumn(
                name: "CommissionSessionId",
                table: "Tenders");
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250811151616_Add_FiscalYear_CommissionSession_User_Role")]
    partial class Add_FiscalYear_CommissionSession_User_Role
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("AppRoles");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTime?>("LastLoginDate")
                        .HasColumnType("datetime2");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AppUsers");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId");

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Add_FiscalYear_CommissionSession_User_Role_Seed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "AppRoles",
                columns: new[] { "Id", "CreatedAt", "IsActive", "IsDeleted", "IsSystem", "Name", "UpdatedAt" },
                values: new object[] { new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(3619), new TimeSpan(0, 0, 0, 0, 0)), true, false, true, "Admin", null });

            migrationBuilder.InsertData(
                table: "FiscalYears",
                columns: new[] { "Id", "CreatedAt", "EndDate", "IsActive", "IsDeleted", "Name", "StartDate", "UpdatedAt" },
                values: new object[] { new Guid("55555555-5555-5555-5555-555555555555"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 519, DateTimeKind.Unspecified).AddTicks(8640), new TimeSpan(0, 0, 0, 0, 0)), new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), true, false, "", new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), null });

            migrationBuilder.InsertData(
                table: "AppUsers",
                columns: new[] { "Id", "CreatedAt", "FullName", "IsActive", "IsDeleted", "LastLoginDate", "PasswordHash", "RoleId", "UpdatedAt", "Username" },
                values: new object[] { new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(6045), new TimeSpan(0, 0, 0, 0, 0)), "System Administrator", true, false, null, "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S", new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_Code",
                table: "Currencies",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Username",
                table: "AppUsers",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Currencies_Code",
                table: "Currencies");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_Username",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_AppRoles_Name",
                table: "AppRoles");

            migrationBuilder.DeleteData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

            migrationBuilder.DeleteData(
                table: "FiscalYears",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"));

            migrationBuilder.DeleteData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "AppRoles");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "AppRoles");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "AppUsers",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "AppRoles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddForeignKey(
                name: "FK_Tenders_CommissionSessions_CommissionSessionId",
                table: "Tenders",
                column: "CommissionSessionId",
                principalTable: "CommissionSessions",
                principalColumn: "Id");
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250811172035_Add_FiscalYear_CommissionSession_User_Role_Seed")]
    partial class Add_FiscalYear_CommissionSession_User_Role_Seed
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<bool>("IsSystem")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("AppRoles");

                    b.HasData(
                        new
                        {
                            Id = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CreatedAt = new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(3619), new TimeSpan(0, 0, 0, 0, 0)),
                            IsActive = true,
                            IsDeleted = false,
                            IsSystem = true,
                            Name = "Admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTime?>("LastLoginDate")
                        .HasColumnType("datetime2");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("Username")
                        .IsUnique();

                    b.ToTable("AppUsers");

                    b.HasData(
                        new
                        {
                            Id = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            CreatedAt = new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(6045), new TimeSpan(0, 0, 0, 0, 0)),
                            FullName = "System Administrator",
                            IsActive = true,
                            IsDeleted = false,
                            PasswordHash = "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S",
                            RoleId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Username = "admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");

                    b.HasData(
                        new
                        {
                            Id = new Guid("55555555-5555-5555-5555-555555555555"),
                            CreatedAt = new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 519, DateTimeKind.Unspecified).AddTicks(8640), new TimeSpan(0, 0, 0, 0, 0)),
                            EndDate = new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified),
                            IsActive = true,
                            IsDeleted = false,
                            Name = "",
                            StartDate = new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified)
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("IsDeleted")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique();

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Fix_Static_Seed_And_Defaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Tenders",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Tenders",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Proposals",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Proposals",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "FiscalYears",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "FiscalYears",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "EvaluationResults",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "EvaluationResults",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Companies",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Companies",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "CommissionSessions",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CommissionSessions",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Bidders",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Bidders",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AppUsers",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AppRoles",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "FiscalYears",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Tenders",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Tenders",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Proposals",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Proposals",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "FiscalYears",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "FiscalYears",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "EvaluationResults",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "EvaluationResults",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Companies",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Companies",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "CommissionSessions",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CommissionSessions",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "Bidders",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Bidders",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AppUsers",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AlterColumn<bool>(
                name: "IsDeleted",
                table: "AppRoles",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: false);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "AppRoles",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldDefaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.UpdateData(
                table: "AppRoles",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(3619), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 520, DateTimeKind.Unspecified).AddTicks(6045), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.UpdateData(
                table: "FiscalYears",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "CreatedAt",
                value: new DateTimeOffset(new DateTime(2025, 8, 11, 17, 20, 31, 519, DateTimeKind.Unspecified).AddTicks(8640), new TimeSpan(0, 0, 0, 0, 0)));
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250811173541_Fix_Static_Seed_And_Defaults")]
    partial class Fix_Static_Seed_And_Defaults
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<bool>("IsSystem")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("AppRoles");

                    b.HasData(
                        new
                        {
                            Id = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            IsActive = true,
                            IsDeleted = false,
                            IsSystem = true,
                            Name = "Admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime?>("LastLoginDate")
                        .HasColumnType("datetime2");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("Username")
                        .IsUnique();

                    b.ToTable("AppUsers");

                    b.HasData(
                        new
                        {
                            Id = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            FullName = "System Administrator",
                            IsActive = true,
                            IsDeleted = false,
                            PasswordHash = "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S",
                            RoleId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Username = "admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");

                    b.HasData(
                        new
                        {
                            Id = new Guid("55555555-5555-5555-5555-555555555555"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            EndDate = new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified),
                            IsActive = true,
                            IsDeleted = false,
                            Name = "",
                            StartDate = new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified)
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique();

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Add_AppUser_MustChangePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                column: "MustChangePassword",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AppUsers");
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250811174256_Add_AppUser_MustChangePassword")]
    partial class Add_AppUser_MustChangePassword
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<bool>("IsSystem")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("AppRoles");

                    b.HasData(
                        new
                        {
                            Id = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            IsActive = true,
                            IsDeleted = false,
                            IsSystem = true,
                            Name = "Admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime?>("LastLoginDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("MustChangePassword")
                        .HasColumnType("bit");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("Username")
                        .IsUnique();

                    b.ToTable("AppUsers");

                    b.HasData(
                        new
                        {
                            Id = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            FullName = "System Administrator",
                            IsActive = true,
                            IsDeleted = false,
                            MustChangePassword = true,
                            PasswordHash = "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S",
                            RoleId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Username = "admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");

                    b.HasData(
                        new
                        {
                            Id = new Guid("55555555-5555-5555-5555-555555555555"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            EndDate = new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified),
                            IsActive = true,
                            IsDeleted = false,
                            Name = "",
                            StartDate = new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified)
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique();

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    /// <inheritdoc />
    public partial class Change_LastLoginDate_To_DateTimeOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastLoginDate",
                table: "AppUsers",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                column: "LastLoginDate",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "LastLoginDate",
                table: "AppUsers",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "AppUsers",
                keyColumn: "Id",
                keyValue: new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                column: "LastLoginDate",
                value: null);
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    [Migration("20250811202716_Change_LastLoginDate_To_DateTimeOffset")]
    partial class Change_LastLoginDate_To_DateTimeOffset
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<bool>("IsSystem")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("AppRoles");

                    b.HasData(
                        new
                        {
                            Id = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            IsActive = true,
                            IsDeleted = false,
                            IsSystem = true,
                            Name = "Admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("LastLoginDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("MustChangePassword")
                        .HasColumnType("bit");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("Username")
                        .IsUnique();

                    b.ToTable("AppUsers");

                    b.HasData(
                        new
                        {
                            Id = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            FullName = "System Administrator",
                            IsActive = true,
                            IsDeleted = false,
                            MustChangePassword = true,
                            PasswordHash = "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S",
                            RoleId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Username = "admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");

                    b.HasData(
                        new
                        {
                            Id = new Guid("55555555-5555-5555-5555-555555555555"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            EndDate = new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified),
                            IsActive = true,
                            IsDeleted = false,
                            Name = "",
                            StartDate = new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified)
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique();

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
// <auto-generated />
using System;
using FinancialEvaluationApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace FinancialEvaluationApp.Migrations
{
    [DbContext(typeof(FinancialEvaluationDbContext))]
    partial class FinancialEvaluationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.8")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<bool>("IsSystem")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("Name")
                        .IsUnique();

                    b.ToTable("AppRoles");

                    b.HasData(
                        new
                        {
                            Id = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            IsActive = true,
                            IsDeleted = false,
                            IsSystem = true,
                            Name = "Admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("FullName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("LastLoginDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<bool>("MustChangePassword")
                        .HasColumnType("bit");

                    b.Property<string>("PasswordHash")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<Guid?>("RoleId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Username")
                        .IsRequired()
                        .HasColumnType("nvarchar(450)");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.HasIndex("Username")
                        .IsUnique();

                    b.ToTable("AppUsers");

                    b.HasData(
                        new
                        {
                            Id = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            FullName = "System Administrator",
                            IsActive = true,
                            IsDeleted = false,
                            MustChangePassword = true,
                            PasswordHash = "$2a$11$7WZq9v8m2fX2b9rXrVjZzO8a1yCqC6c7nG3Z1m0i9xUj2QxQyXj/S",
                            RoleId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                            Username = "admin"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid?>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<string>("NationalId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CompanyId");

                    b.ToTable("Bidders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<string>("Description")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTime>("SessionDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("CommissionSessions");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<Guid?>("ParentCompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("RegistrationNo")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("ParentCompanyId");

                    b.ToTable("Companies");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal>("FinalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Notes")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<decimal>("PriceScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<int?>("Rank")
                        .HasColumnType("int");

                    b.Property<decimal>("TechnicalScore")
                        .HasColumnType("decimal(18,4)");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("TenderId");

                    b.ToTable("EvaluationResults");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.FiscalYear", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<DateTime>("EndDate")
                        .HasColumnType("datetime2");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<DateTime>("StartDate")
                        .HasColumnType("datetime2");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.ToTable("FiscalYears");

                    b.HasData(
                        new
                        {
                            Id = new Guid("55555555-5555-5555-5555-555555555555"),
                            CreatedAt = new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                            EndDate = new DateTime(2026, 3, 20, 0, 0, 0, 0, DateTimeKind.Unspecified),
                            IsActive = true,
                            IsDeleted = false,
                            Name = "",
                            StartDate = new DateTime(2025, 3, 21, 0, 0, 0, 0, DateTimeKind.Unspecified)
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BidderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<decimal?>("ForeignAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<Guid?>("ForeignCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("FxRate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<decimal?>("NormalizedAmountIRR")
                        .HasColumnType("decimal(18,2)");

                    b.Property<string>("ProposalNo")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("nvarchar(100)");

                    b.Property<decimal?>("RialAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset>("SubmittedAt")
                        .HasColumnType("datetimeoffset");

                    b.Property<Guid>("TenderId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<int>("Type")
                        .HasColumnType("int");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("BidderId");

                    b.HasIndex("ForeignCurrencyId");

                    b.HasIndex("TenderId");

                    b.ToTable("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal?>("BaseEstimateAmount")
                        .HasColumnType("decimal(18,2)");

                    b.Property<DateTimeOffset?>("ClosingDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(150)
                        .HasColumnType("nvarchar(150)");

                    b.Property<Guid?>("CommissionSessionId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("CompanyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<DateTimeOffset>("CreatedAt")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSUTCDATETIME()");

                    b.Property<bool>("IsDeleted")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bit")
                        .HasDefaultValue(false);

                    b.Property<DateTimeOffset?>("PublishDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Status")
                        .HasColumnType("int");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasMaxLength(300)
                        .HasColumnType("nvarchar(300)");

                    b.Property<DateTimeOffset?>("UpdatedAt")
                        .HasColumnType("datetimeoffset");

                    b.HasKey("Id");

                    b.HasIndex("CommissionSessionId");

                    b.HasIndex("CompanyId");

                    b.ToTable("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.Currency", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<string>("Code")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("nvarchar(50)");

                    b.Property<decimal?>("DefaultFxRateToIRR")
                        .HasColumnType("decimal(18,6)");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<bool>("IsDefault")
                        .HasColumnType("bit");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(200)
                        .HasColumnType("nvarchar(200)");

                    b.Property<int>("SortOrder")
                        .HasColumnType("int");

                    b.Property<string>("Symbol")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("Code")
                        .IsUnique();

                    b.ToTable("Currencies");

                    b.HasData(
                        new
                        {
                            Id = new Guid("11111111-1111-1111-1111-111111111111"),
                            Code = "IRR",
                            IsActive = true,
                            IsDefault = true,
                            Name = "Iranian Rial",
                            SortOrder = 1,
                            Symbol = "﷼"
                        },
                        new
                        {
                            Id = new Guid("22222222-2222-2222-2222-222222222222"),
                            Code = "USD",
                            IsActive = true,
                            IsDefault = false,
                            Name = "US Dollar",
                            SortOrder = 2,
                            Symbol = "$"
                        },
                        new
                        {
                            Id = new Guid("33333333-3333-3333-3333-333333333333"),
                            Code = "EUR",
                            IsActive = true,
                            IsDefault = false,
                            Name = "Euro",
                            SortOrder = 3,
                            Symbol = "€"
                        },
                        new
                        {
                            Id = new Guid("44444444-4444-4444-4444-444444444444"),
                            Code = "AED",
                            IsActive = true,
                            IsDefault = false,
                            Name = "UAE Dirham",
                            SortOrder = 4,
                            Symbol = "AED"
                        });
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uniqueidentifier");

                    b.Property<Guid>("BaseCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<bool>("IsActive")
                        .HasColumnType("bit");

                    b.Property<Guid>("QuoteCurrencyId")
                        .HasColumnType("uniqueidentifier");

                    b.Property<decimal>("Rate")
                        .HasColumnType("decimal(18,6)");

                    b.Property<DateTimeOffset>("RateDate")
                        .HasColumnType("datetimeoffset");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("Id");

                    b.HasIndex("BaseCurrencyId");

                    b.HasIndex("QuoteCurrencyId");

                    b.ToTable("FxRates");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppUser", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.AppRole", "Role")
                        .WithMany("Users")
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("Role");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Bidders")
                        .HasForeignKey("CompanyId");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "ParentCompany")
                        .WithMany("Subsidiaries")
                        .HasForeignKey("ParentCompanyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.Navigation("ParentCompany");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.EvaluationResult", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("EvaluationResults")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Proposal", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.Bidder", "Bidder")
                        .WithMany("Proposals")
                        .HasForeignKey("BidderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "ForeignCurrency")
                        .WithMany()
                        .HasForeignKey("ForeignCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Tender", "Tender")
                        .WithMany("Proposals")
                        .HasForeignKey("TenderId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Bidder");

                    b.Navigation("ForeignCurrency");

                    b.Navigation("Tender");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.Core.CommissionSession", "CommissionSession")
                        .WithMany("Tenders")
                        .HasForeignKey("CommissionSessionId")
                        .OnDelete(DeleteBehavior.Restrict);

                    b.HasOne("FinancialEvaluationApp.Models.Core.Company", "Company")
                        .WithMany("Tenders")
                        .HasForeignKey("CompanyId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("CommissionSession");

                    b.Navigation("Company");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.RefData.FxRate", b =>
                {
                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "BaseCurrency")
                        .WithMany()
                        .HasForeignKey("BaseCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.HasOne("FinancialEvaluationApp.Models.RefData.Currency", "QuoteCurrency")
                        .WithMany()
                        .HasForeignKey("QuoteCurrencyId")
                        .OnDelete(DeleteBehavior.Restrict)
                        .IsRequired();

                    b.Navigation("BaseCurrency");

                    b.Navigation("QuoteCurrency");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.AppRole", b =>
                {
                    b.Navigation("Users");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Bidder", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.CommissionSession", b =>
                {
                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Company", b =>
                {
                    b.Navigation("Bidders");

                    b.Navigation("Subsidiaries");

                    b.Navigation("Tenders");
                });

            modelBuilder.Entity("FinancialEvaluationApp.Models.Core.Tender", b =>
                {
                    b.Navigation("EvaluationResults");

                    b.Navigation("Proposals");
                });
#pragma warning restore 612, 618
        }
    }
}
namespace FinancialEvaluationApp.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class AppRole : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public bool IsSystem { get; set; } // نقش سیستمی که کاربر نمی‌تواند حذف یا تغییر نام دهد
        public bool IsActive { get; set; } // فعال یا غیرفعال بودن نقش

        public ICollection<AppUser> Users { get; set; } = new List<AppUser>();

    }
}
using System;

namespace FinancialEvaluationApp.Models.Core
{
    public class AppUser : BaseEntity
    {
        public string Username { get; set; }=string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;    
        public bool IsActive { get; set; }=true;
        public DateTimeOffset? LastLoginDate { get; set; }

        public bool MustChangePassword { get; set; } = false;

        public Guid? RoleId { get; set; }
        public AppRole Role { get; set; } = null!;
    }
}
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

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Bidder : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }=string.Empty;

        [MaxLength(50)]
        public string NationalId { get; set; }=string.Empty ;

        public Guid? CompanyId { get; set; }
        public Company? Company { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
    }
}
using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class CommissionSession : BaseEntity
    {
        public DateTime SessionDate { get; set; }
        public string Description { get; set; } = string.Empty;

        // ارتباط با مناقصات
        public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Company : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }=string.Empty;

        [MaxLength(50)]
        public string RegistrationNo { get; set; }=string.Empty ;

        public Guid? ParentCompanyId { get; set; }
        public Company? ParentCompany { get; set; }

        public ICollection<Company> Subsidiaries { get; set; } = new List<Company>();
        public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
        public ICollection<Bidder> Bidders { get; set; } = new List<Bidder>();
    }
}
namespace FinancialEvaluationApp.Models.Core
{
    public enum ProposalType
    {
        Rial = 1,
        Foreign = 2,
        Mixed = 3
    }

    public enum TenderStatus
    {
        Draft = 1,
        Published = 2,
        Closed = 3,
        Evaluated = 4,
        Awarded = 5,
        Canceled = 6
    }
}
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.Core
{
    public class EvaluationResult : BaseEntity
    {
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; } = null!;

        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; } = null!;     

        [Column(TypeName = "decimal(18,4)")]
        public decimal PriceScore { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal TechnicalScore { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal FinalScore { get; set; }

        public int? Rank { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
using System;
using System.Collections.Generic;

namespace FinancialEvaluationApp.Models.Core
{
    public class FiscalYear : BaseEntity
    {
        public string Name { get; set; } = string.Empty; // مثال: 1403-1404
        public DateTime StartDate { get; set; } // تاریخ شروع (میلادی، نمایش شمسی)
        public DateTime EndDate { get; set; }   // تاریخ پایان
        public bool IsActive { get; set; }
    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Models.Core
{
    public class Proposal : BaseEntity
    {
        [Required]
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; } = null!;

        [Required]
        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; } = null!;

        [Required]
        public ProposalType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? RialAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ForeignAmount { get; set; }

        public Guid? ForeignCurrencyId { get; set; }
        public Currency ForeignCurrency { get; set; } = null!;

        [Column(TypeName = "decimal(18,6)")]
        public decimal? FxRate { get; set; }

        [MaxLength(100)]
        public string ProposalNo { get; set; } =string.Empty; 

        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal? NormalizedAmountIRR { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Tender : BaseEntity
    {
        [Required, MaxLength(150)]
        public string Code { get; set; }=string.Empty;

        [Required, MaxLength(300)]
        public string Title { get; set; }=string.Empty; 

        public TenderStatus Status { get; set; } = TenderStatus.Draft;

        [Required]
        public Guid CompanyId { get; set; }
        public Company? Company { get; set; }

        public DateTimeOffset? PublishDate { get; set; }
        public DateTimeOffset? ClosingDate { get; set; }

        public decimal? BaseEstimateAmount { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
        public Guid? CommissionSessionId { get; set; }
        public CommissionSession CommissionSession { get; set; } = null!;

    }
}
using System;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.RefData
{
    public abstract class BaseLookup
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(50)]
        public string Code { get; set; }=string.Empty;

        [Required, MaxLength(200)]
        public string Name { get; set; }=string.Empty ;

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 0;
    }
}
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.RefData
{
    public class Currency : BaseLookup
    {
        public string Symbol { get; set; }=string.Empty;

        [Column(TypeName = "decimal(18,6)")]
        public decimal? DefaultFxRateToIRR { get; set; }

        public bool IsDefault { get; set; } = false;
    }
}
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.RefData
{
    public class FxRate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BaseCurrencyId { get; set; }
        public Currency BaseCurrency { get; set; } = null!;

        public Guid QuoteCurrencyId { get; set; }
        public Currency QuoteCurrency { get; set; } = null!;

        public DateTimeOffset RateDate { get; set; }

        [Column(TypeName = "decimal(18,6)")]
        public decimal Rate { get; set; }

        public string Source { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.ViewModels
{
    public class LoginVm
    {
        [Required, StringLength(128)]
        public string Username { get; set; } = string.Empty;

        [Required, StringLength(128)]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    public class ChangePasswordVm
    {
        [Required, StringLength(128)]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required, StringLength(128), MinLength(12)]
        public string NewPassword { get; set; } = string.Empty;

        [Compare(nameof(NewPassword))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.ViewModels.Accounts
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "رمز فعلی را وارد کنید")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز فعلی")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "رمز جدید را وارد کنید")]
        [MinLength(12, ErrorMessage = "حداقل ۱۲ کاراکتر")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز جدید")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "تکرار رمز جدید را وارد کنید")]
        [DataType(DataType.Password)]
        [Display(Name = "تکرار رمز جدید")]
        [Compare(nameof(NewPassword), ErrorMessage = "تکرار رمز با رمز جدید یکسان نیست")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.ViewModels.Accounts
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "لطفاً نام کاربری را وارد کنید")]
        [Display(Name = "نام کاربری")]
        public string Username { get; set; }=string.Empty;  

        [Required(ErrorMessage = "لطفاً رمز عبور را وارد کنید")]
        [DataType(DataType.Password)]
        [Display(Name = "رمز عبور")]
        public string Password { get; set; }= string.Empty; 

        [Display(Name = "مرا به خاطر بسپار")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
using System;

namespace FinancialEvaluationApp.ViewModels.Users
{
    public class UserListItemVm
    {
        public Guid Id { get; set; }                 // از BaseEntity
        public string Username { get; set; } = "";
        public string FullName { get; set; } = "";
        public string RoleName { get; set; } = "—";
        public bool IsActive { get; set; }
        public string? LastLoginDisplay { get; set; } // فقط برای نمایش
    }
}
// <autogenerated />
using System;
using System.Reflection;
[assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(".NETCoreApp,Version=v8.0", FrameworkDisplayName = ".NET 8.0")]
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Reflection;

[assembly: System.Reflection.AssemblyCompanyAttribute("FinancialEvaluationApp")]
[assembly: System.Reflection.AssemblyConfigurationAttribute("Debug")]
[assembly: System.Reflection.AssemblyFileVersionAttribute("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersionAttribute("1.0.0+9b923f2b9bddc258cfa3a3e56edff2942c41ed1d")]
[assembly: System.Reflection.AssemblyProductAttribute("FinancialEvaluationApp")]
[assembly: System.Reflection.AssemblyTitleAttribute("FinancialEvaluationApp")]
[assembly: System.Reflection.AssemblyVersionAttribute("1.0.0.0")]

// Generated by the MSBuild WriteCodeFragment class.

// <auto-generated/>
global using global::Microsoft.AspNetCore.Builder;
global using global::Microsoft.AspNetCore.Hosting;
global using global::Microsoft.AspNetCore.Http;
global using global::Microsoft.AspNetCore.Routing;
global using global::Microsoft.Extensions.Configuration;
global using global::Microsoft.Extensions.DependencyInjection;
global using global::Microsoft.Extensions.Hosting;
global using global::Microsoft.Extensions.Logging;
global using global::System;
global using global::System.Collections.Generic;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Net.Http;
global using global::System.Net.Http.Json;
global using global::System.Threading;
global using global::System.Threading.Tasks;
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using System;
using System.Reflection;

[assembly: Microsoft.AspNetCore.Mvc.ApplicationParts.ProvideApplicationPartFactoryAttribute("Microsoft.AspNetCore.Mvc.ApplicationParts.ConsolidatedAssemblyApplicationPartFact" +
    "ory, Microsoft.AspNetCore.Mvc.Razor")]

// Generated by the MSBuild WriteCodeFragment class.

using Microsoft.Extensions.Caching.Memory;

public interface ILoginThrottle
{
    bool IsLocked(string username, string ip, out TimeSpan? retryAfter);
    void RegisterFail(string username, string ip);
    void RegisterSuccess(string username, string ip);
}

public class MemoryLoginThrottle : ILoginThrottle
{
    private readonly IMemoryCache _cache;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(10);

    public MemoryLoginThrottle(IMemoryCache cache) => _cache = cache;

    private static string Key(string u, string ip) => $"login:{u}:{ip}".ToLowerInvariant();

    public bool IsLocked(string username, string ip, out TimeSpan? retryAfter)
    {
        var key = Key(username, ip);
        if (_cache.TryGetValue<(int fails, DateTimeOffset? lockedUntil)>(key, out var state)
            && state.lockedUntil.HasValue)
        {
            var remain = state.lockedUntil.Value - DateTimeOffset.UtcNow;
            if (remain > TimeSpan.Zero) { retryAfter = remain; return true; }
        }
        retryAfter = null;
        return false;
    }

    public void RegisterFail(string username, string ip)
    {
        var key = Key(username, ip);
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue<(int fails, DateTimeOffset? lockedUntil)>(key, out var state))
        {
            state.fails++;
            if (state.fails >= MaxAttempts)
                state.lockedUntil = now.Add(Lockout);

            _cache.Set(key, state, Window);
        }
        else
        {
            _cache.Set(key, (1, (DateTimeOffset?)null), Window);
        }
    }

    public void RegisterSuccess(string username, string ip)
    {
        _cache.Remove(Key(username, ip));
    }
}
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Services
{
    public interface ILookupService
    {
        Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync();
        Task ClearCurrencyCacheAsync();
    }

    public class LookupService : ILookupService
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly IMemoryCache _cache;
        private const string CurrencyCacheKey = "Lookup.Currencies.Active";

        public LookupService(FinancialEvaluationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync()
        {
            if (_cache.TryGetValue(CurrencyCacheKey, out IReadOnlyList<Currency>? cached) && cached is not null)
                return cached;

            var list = await _db.Currencies
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync();

            IReadOnlyList<Currency> result = list; // non-null
            _cache.Set(CurrencyCacheKey, result, TimeSpan.FromMinutes(30));
            return result;
        }


        public Task ClearCurrencyCacheAsync()
        {
            _cache.Remove(CurrencyCacheKey);
            return Task.CompletedTask;
        }
    }
}
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
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Bidder : BaseEntity
    {
        [Required, MaxLength(200)]
        public string Name { get; set; }

        [MaxLength(50)]
        public string NationalId { get; set; }

        public Guid? CompanyId { get; set; }
        public Company Company { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
    }
}
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
namespace FinancialEvaluationApp.Models.Core
{
    public enum ProposalType
    {
        Rial = 1, Foreign = 2, Mixed = 3
    }

    public enum TenderStatus
    {
        Draft = 1, Published = 2, Closed = 3, Evaluated = 4, Awarded = 5, Canceled = 6
    }
}
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.Core
{
    public class EvaluationResult : BaseEntity
    {
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; }

        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; }

        [Column(TypeName = ""decimal(18,4)"")]
        public decimal PriceScore { get; set; }

        [Column(TypeName = ""decimal(18,4)"")]
        public decimal TechnicalScore { get; set; }

        [Column(TypeName = ""decimal(18,4)"")]
        public decimal FinalScore { get; set; }

        public int? Rank { get; set; }
        public string Notes { get; set; }
    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Models.Core
{
    public class Proposal : BaseEntity
    {
        [Required]
        public Guid TenderId { get; set; }
        public Tender Tender { get; set; }

        [Required]
        public Guid BidderId { get; set; }
        public Bidder Bidder { get; set; }

        [Required]
        public ProposalType Type { get; set; }

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? RialAmount { get; set; }

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? ForeignAmount { get; set; }

        // به‌جاي enum، کليد خارجي به جدول Currency
        public Guid? ForeignCurrencyId { get; set; }
        public Currency ForeignCurrency { get; set; }

        [Column(TypeName = ""decimal(18,6)"")]
        public decimal? FxRate { get; set; }

        [MaxLength(100)]
        public string ProposalNo { get; set; }

        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = ""decimal(18,2)"")]
        public decimal? NormalizedAmountIRR { get; set; }
    }
}
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FinancialEvaluationApp.Models.Core
{
    public class Tender : BaseEntity
    {
        [Required, MaxLength(150)]
        public string Code { get; set; }

        [Required, MaxLength(300)]
        public string Title { get; set; }

        public TenderStatus Status { get; set; } = TenderStatus.Draft;

        [Required]
        public Guid CompanyId { get; set; }
        public Company Company { get; set; }

        public DateTimeOffset? PublishDate { get; set; }
        public DateTimeOffset? ClosingDate { get; set; }

        public decimal? BaseEstimateAmount { get; set; }

        public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
        public ICollection<EvaluationResult> EvaluationResults { get; set; } = new List<EvaluationResult>();
    }
}
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
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinancialEvaluationApp.Models.RefData
{
    public class FxRate
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid BaseCurrencyId { get; set; }
        public Currency BaseCurrency { get; set; }

        public Guid QuoteCurrencyId { get; set; }
        public Currency QuoteCurrency { get; set; }

        public DateTimeOffset RateDate { get; set; }

        [Column(TypeName = ""decimal(18,6)"")]
        public decimal Rate { get; set; }

        public string Source { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Services
{
    public interface ILookupService
    {
        Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync();
    }

    public class LookupService : ILookupService
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly IMemoryCache _cache;
        private const string CurrencyCacheKey = ""Lookup.Currencies.Active"";

        public LookupService(FinancialEvaluationDbContext db, IMemoryCache cache)
        {
            _db = db; _cache = cache;
        }

        public async Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync()
        {
            if (_cache.TryGetValue(CurrencyCacheKey, out IReadOnlyList<Currency> cached))
                return cached;

            var list = await _db.Currencies
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .AsNoTracking().ToListAsync();

            _cache.Set(CurrencyCacheKey, list, TimeSpan.FromMinutes(30));
            return list;
        }
    }
}
