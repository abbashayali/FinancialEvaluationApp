using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.Core;
using FinancialEvaluationApp.ViewModels.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FinancialEvaluationApp.Controllers
{
    [Authorize] // همه اکشن‌ها نیازمند ورودند
    public class UsersController : Controller
    {
        private readonly FinancialEvaluationDbContext _db;
        public UsersController(FinancialEvaluationDbContext db) => _db = db;

        // ---------- Helper ----------
        private async Task<List<SelectListItem>> GetRolesAsync() =>
            await _db.AppRoles.AsNoTracking().OrderBy(r => r.Name)
                .Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name })
                .ToListAsync();

        private async Task<Guid?> GetRoleIdAsync(string roleName) =>
            await _db.AppRoles.Where(r => r.Name == roleName).Select(r => (Guid?)r.Id).FirstOrDefaultAsync();

        private static string GenerateTempPassword(int length = 16)
        {
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digits = "23456789";
            const string symbols = "!@#$%^&*_-+=?";
            var all = lower + upper + digits + symbols;

            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = all[bytes[i] % all.Length];
            chars[0] = lower[bytes[0] % lower.Length];
            chars[1] = upper[bytes[1] % upper.Length];
            chars[2] = digits[bytes[2] % digits.Length];
            chars[3] = symbols[bytes[3] % symbols.Length];
            return new string(chars);
        }

        // ---------- Index: Admin + Manager ----------
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Index(string? sort = "username", string? dir = "asc")
        {
            sort = (sort ?? "username").ToLowerInvariant();
            dir = (dir ?? "asc").ToLowerInvariant();

            var query = _db.AppUsers.Include(u => u.Role).AsNoTracking();
            bool asc = dir != "desc";

            query = sort switch
            {
                "fullname" => asc ? query.OrderBy(u => u.FullName) : query.OrderByDescending(u => u.FullName),
                "role" => asc ? query.OrderBy(u => u.Role != null ? u.Role.Name : "")
                                   : query.OrderByDescending(u => u.Role != null ? u.Role.Name : ""),
                "lastlogin" => asc ? query.OrderBy(u => u.LastLoginDate) : query.OrderByDescending(u => u.LastLoginDate),
                "status" => asc ? query.OrderBy(u => u.IsActive) : query.OrderByDescending(u => u.IsActive),
                _ => asc ? query.OrderBy(u => u.Username) : query.OrderByDescending(u => u.Username),
            };

            var users = await query.Select(u => new UserListItemVm
            {
                Id = u.Id,
                Username = u.Username ?? "",
                FullName = u.FullName ?? "",
                RoleName = u.Role != null ? u.Role.Name : "—",
                IsActive = u.IsActive,
                LastLoginDisplay = u.LastLoginDate.HasValue
                    ? u.LastLoginDate.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
                    : null
            }).ToListAsync();

            ViewBag.Sort = sort; ViewBag.Dir = dir;
            return View(users);
        }

        // ---------- Create: Admin + Manager ----------
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Roles = await GetRolesAsync();
            return View(new UserCreateVm());
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateVm vm)
        {
            ViewBag.Roles = await GetRolesAsync();
            if (!ModelState.IsValid) return View(vm);

            var adminRoleId = await GetRoleIdAsync("Admin");
            var managerRoleId = await GetRoleIdAsync("Manager");
            var userRoleId = await GetRoleIdAsync("User");

            if (vm.RoleId == Guid.Empty)
            {
                ModelState.AddModelError(nameof(vm.RoleId), "نقش انتخاب‌شده معتبر نیست.");
                return View(vm);
            }

            // محدودیت نقش سازنده
            if (User.IsInRole("Manager"))
            {
                // منیجر فقط می‌تواند User یا Manager بسازد
                if (adminRoleId.HasValue && vm.RoleId == adminRoleId.Value)
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "منیجر مجاز به ایجاد کاربر Admin نیست.");
                    return View(vm);
                }
                if (!(vm.RoleId == managerRoleId || vm.RoleId == userRoleId))
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "نقش انتخاب‌شده برای منیجر مجاز نیست.");
                    return View(vm);
                }
            }
            else if (User.IsInRole("Admin"))
            {
                // ادمین می‌تواند User/Manager بسازد اما ادمینِ جدید نه (تک‌ادمین)
                if (adminRoleId.HasValue && vm.RoleId == adminRoleId.Value)
                {
                    var adminCount = await _db.AppUsers.CountAsync(u => u.RoleId == adminRoleId.Value);
                    if (adminCount > 0)
                    {
                        ModelState.AddModelError(nameof(vm.RoleId), "در سیستم تنها یک مدیر (Admin) مجاز است.");
                        return View(vm);
                    }
                }
            }

            // یکتایی نام کاربری
            var username = (vm.Username ?? "").Trim();
            var key = username.ToLowerInvariant();
            var exists = await _db.AppUsers.AnyAsync(u => u.Username.ToLower() == key);
            if (exists)
            {
                ModelState.AddModelError(nameof(vm.Username), "نام کاربری قبلاً وجود دارد.");
                return View(vm);
            }

            // سیاست رمز
            if (string.IsNullOrEmpty(vm.TemporaryPassword) || vm.TemporaryPassword.Length < 12)
            {
                ModelState.AddModelError(nameof(vm.TemporaryPassword), "گذرواژه موقت باید حداقل ۱۲ کاراکتر باشد.");
                return View(vm);
            }

            const int WorkFactor = 12;
            var hash = BCrypt.Net.BCrypt.HashPassword(vm.TemporaryPassword, workFactor: WorkFactor);

            var user = new AppUser
            {
                Username = username,
                FullName = (vm.FullName ?? "").Trim(),
                PasswordHash = hash,
                RoleId = vm.RoleId,
                IsActive = vm.IsActive,
                MustChangePassword = vm.MustChangePassword,
                LastLoginDate = null
            };

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();

            TempData["Success"] = "کاربر با موفقیت ایجاد شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Edit: Admin + Manager (قوانین نقش) ----------
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Edit(Guid id)
        {
            var u = await _db.AppUsers.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) { TempData["Error"] = "کاربر پیدا نشد."; return RedirectToAction(nameof(Index)); }

            // منیجر حق ویرایش Admin را ندارد
            if (User.IsInRole("Manager"))
            {
                var adminRoleId = await GetRoleIdAsync("Admin");
                if (adminRoleId.HasValue && u.RoleId == adminRoleId.Value)
                {
                    TempData["Error"] = "دسترسی کافی ندارید: ویرایش مدیر سیستم مجاز نیست.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var vm = new UserEditVm
            {
                Id = u.Id,
                Username = u.Username ?? "",
                FullName = u.FullName ?? "",
                RoleId = u.RoleId,
                IsActive = u.IsActive,
                MustChangePassword = u.MustChangePassword
            };

            ViewBag.Roles = await GetRolesAsync();
            return View(vm);
        }

        [Authorize(Roles = "Admin,Manager")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, UserEditVm vm)
        {
            ViewBag.Roles = await GetRolesAsync();
            if (id != vm.Id) return BadRequest();
            if (!ModelState.IsValid) return View(vm);

            var user = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == id);
            if (user == null) { TempData["Error"] = "کاربر پیدا نشد."; return RedirectToAction(nameof(Index)); }

            var adminRoleId = await GetRoleIdAsync("Admin");
            var managerRoleId = await GetRoleIdAsync("Manager");
            var userRoleId = await GetRoleIdAsync("User");

            if (vm.RoleId == null)
            {
                ModelState.AddModelError(nameof(vm.RoleId), "نقش انتخاب‌شده معتبر نیست.");
                return View(vm);
            }

            bool isTargetAdminNow = adminRoleId.HasValue && user.RoleId == adminRoleId.Value;
            bool willBeAdmin = adminRoleId.HasValue && vm.RoleId.Value == adminRoleId.Value;

            if (User.IsInRole("Admin"))
            {
                // ادمین می‌تواند بین User/Manager جابه‌جا کند؛
                // اما اجازه ارتقا کسی به Admin ندارد و نمی‌تواند Admin فعلی را از Admin خارج کند.
                if (!isTargetAdminNow && willBeAdmin)
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "نقش Admin فقط یک نفر است؛ ارتقای کاربر به Admin مجاز نیست.");
                    return View(vm);
                }
                if (isTargetAdminNow && !willBeAdmin)
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "نقش مدیر سیستم قابل تغییر نیست.");
                    return View(vm);
                }
            }
            else if (User.IsInRole("Manager"))
            {
                // منیجر: نه ویرایش Admin، نه تخصیص Admin
                if (isTargetAdminNow)
                {
                    ModelState.AddModelError("", "دسترسی کافی ندارید: ویرایش مدیر سیستم مجاز نیست.");
                    return View(vm);
                }
                if (willBeAdmin)
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "منیجر مجاز به تخصیص نقش Admin نیست.");
                    return View(vm);
                }
                // منیجر می‌تواند بین User/Manager جابه‌جا کند
                if (!(vm.RoleId.Value == managerRoleId || vm.RoleId.Value == userRoleId))
                {
                    ModelState.AddModelError(nameof(vm.RoleId), "منیجر فقط می‌تواند بین User و Manager تغییر نقش دهد.");
                    return View(vm);
                }
            }

            user.FullName = (vm.FullName ?? "").Trim();
            user.RoleId = vm.RoleId.Value;
            user.IsActive = vm.IsActive;
            user.MustChangePassword = vm.MustChangePassword;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            TempData["Success"] = "اطلاعات کاربر به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Delete: فقط Admin (و Admin حذف نشود) ----------
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var u = await _db.AppUsers.AsNoTracking().Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) { TempData["Error"] = "کاربر پیدا نشد."; return RedirectToAction(nameof(Index)); }

            var adminRoleId = await GetRoleIdAsync("Admin");
            if (adminRoleId.HasValue && u.RoleId == adminRoleId.Value)
            {
                TempData["Error"] = "کاربر Admin قابل حذف نیست.";
                return RedirectToAction(nameof(Index));
            }

            return View(u);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var user = await _db.AppUsers.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (user == null) { TempData["Error"] = "کاربر پیدا نشد."; return RedirectToAction(nameof(Index)); }

            var adminRoleId = await GetRoleIdAsync("Admin");
            if (adminRoleId.HasValue && user.RoleId == adminRoleId.Value)
            {
                TempData["Error"] = "کاربر Admin قابل حذف نیست.";
                return RedirectToAction(nameof(Index));
            }

            var selfIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(selfIdStr, out var selfId) && selfId == user.Id)
            {
                TempData["Error"] = "نمی‌توانید حساب کاربری خودتان را حذف کنید.";
                return RedirectToAction(nameof(Index));
            }

            _db.AppUsers.Remove(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = "کاربر حذف شد.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Reset Password ----------
        // Admin: برای همه به‌جز ردیف Admin (در UI مخفی است؛ اگر خواستی می‌توانیم سمت سرور هم ممنوع کنیم)
        // Manager: فقط برای User
        [Authorize(Roles = "Admin,Manager")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(Guid id)
        {
            var u = await _db.AppUsers.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) { TempData["Error"] = "کاربر پیدا نشد."; return RedirectToAction(nameof(Index)); }

            var roleName = u.Role?.Name ?? "";
            var isManager = User.IsInRole("Manager");

            if (isManager && !string.Equals(roleName, "User", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "دسترسی کافی ندارید: منیجر فقط می‌تواند گذرواژهٔ کاربران عادی را ریست کند.";
                return RedirectToAction(nameof(Index));
            }

            var temp = GenerateTempPassword(16);
            const int WorkFactor = 12;
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temp, workFactor: WorkFactor);
            u.MustChangePassword = true;
            u.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();

            var vm = new ResetPasswordResultVm
            {
                UserId = u.Id,
                Username = u.Username ?? "",
                FullName = u.FullName ?? "",
                RoleName = roleName,
                TemporaryPassword = temp
            };
            return View("ResetPasswordResult", vm);
        }
    }
}
