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
    // Authorize سراسری فعال است؛ این کنترلر پیش‌فرض Anonymous دارد مگر جایی که [Authorize] خورده
    [AllowAnonymous]
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

            // نرمال‌سازی یوزرنیم (رفع هشدار نال + امنیت)
            var rawUsername = (vm.Username ?? string.Empty).Trim();
            var usernameKey = rawUsername.ToLowerInvariant();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (_throttle.IsLocked(usernameKey, ip, out var retry))
            {
                var mins = retry.HasValue ? (int)retry.Value.TotalMinutes : 1;
                ModelState.AddModelError("", $"حساب موقتاً قفل شد. بعد از {mins} دقیقه دوباره تلاش کنید.");
                return View(vm);
            }

            // جستجوی کاربر فعال با یوزرنیم نرمال‌شده
            var user = await _db.AppUsers
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.IsActive && u.Username.ToLower() == usernameKey);

            if (user == null || !BCrypt.Net.BCrypt.Verify(vm.Password ?? string.Empty, user.PasswordHash))
            {
                _throttle.RegisterFail(usernameKey, ip);
                ModelState.AddModelError("", "نام کاربری یا رمز عبور اشتباه است.");
                return View(vm);
            }

            _throttle.RegisterSuccess(usernameKey, ip);

            // Claims ایمن (رفع هشدارهای نال)
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName),
                new Claim(ClaimTypes.Role, user.Role?.Name ?? "User"),
                new Claim("username", user.Username)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // اگر باید پسورد را عوض کند، بلافاصله هدایتش کن
            if (user.MustChangePassword)
                return RedirectToAction(nameof(ChangePassword));

            // ثبت آخرین ورود
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

            var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(idValue, out var uid)) return Unauthorized();

            var user = await _db.AppUsers.FindAsync(uid);
            if (user is null) return Unauthorized();

            // بررسی رمز فعلی
            if (!BCrypt.Net.BCrypt.Verify(vm.CurrentPassword ?? string.Empty, user.PasswordHash))
            {
                ModelState.AddModelError("", "رمز فعلی درست نیست.");
                return View(vm);
            }

            // سیاست: حداقل ۱۲ کاراکتر
            if (string.IsNullOrEmpty(vm.NewPassword) || vm.NewPassword.Length < 12)
            {
                ModelState.AddModelError("", "رمز جدید باید حداقل ۱۲ کاراکتر باشد.");
                return View(vm);
            }

            // یکسان‌سازی WorkFactor در کل سیستم (پیشنهادی: 12)
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.NewPassword, workFactor: 12);
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
            return RedirectToAction("Login", "Account");
        }

        public IActionResult AccessDenied() => View();
    }
}
