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
