using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using UserAdminKit.Abstractions;

namespace FinancialEvaluationApp.Services
{
    public sealed class EfUserAdminService : IUserAdminService
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly IHttpContextAccessor _ctx;

        public EfUserAdminService(FinancialEvaluationDbContext db, IHttpContextAccessor ctx)
        {
            _db = db; _ctx = ctx;
        }

        private bool IsAdmin() =>
            _ctx.HttpContext?.User.HasClaim("is_admin", "1") == true ||
            _ctx.HttpContext?.User.IsInRole("Admin") == true;

        private bool IsManager() =>
            _ctx.HttpContext?.User.HasClaim("is_manager", "1") == true ||
            _ctx.HttpContext?.User.IsInRole("Manager") == true;

        private Guid? CurrentUserId()
        {
            var id = _ctx.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(id, out var g) ? g : (Guid?)null;
        }

        public async Task<IReadOnlyList<UserListItemVm>> GetUsersAsync(string sort, string dir)
        {
            // 1) حتماً IQueryable نه IIncludableQueryable
            IQueryable<AppUser> q = _db.AppUsers
                .AsNoTracking()
                .Include(u => u.Role);

            // 2) سورت بعد از تمام Include ها
            var key = (sort ?? "username").ToLowerInvariant();
            var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

            q = key switch
            {
                "fullname" => asc ? q.OrderBy(u => u.FullName) : q.OrderByDescending(u => u.FullName),
                "role" => asc ? q.OrderBy(u => u.Role!.Name) : q.OrderByDescending(u => u.Role!.Name),
                "status" => asc ? q.OrderBy(u => u.IsActive) : q.OrderByDescending(u => u.IsActive),
                "lastlogin" => asc ? q.OrderBy(u => u.LastLoginDate) : q.OrderByDescending(u => u.LastLoginDate),
                _ => asc ? q.OrderBy(u => u.Username) : q.OrderByDescending(u => u.Username),
            };

            // 3) پروجکشن: به LastLogin مقدار بده، نه LastLoginDisplay (که read-only است)
            var list = await q.Select(u => new UserListItemVm
            {
                Id = u.Id,
                Username = u.Username ?? "",
                FullName = u.FullName ?? "",
                RoleName = u.Role != null ? u.Role.Name : "-",
                IsActive = u.IsActive,
                LastLogin = u.LastLoginDate.HasValue
    ? u.LastLoginDate.Value.LocalDateTime   // یا .UtcDateTime اگر UTC می‌خواهی
    : (DateTime?)null
            }).ToListAsync();

            return list;
        }

        public async Task<IReadOnlyList<SelectListItem>> GetRoleOptionsAsync()
        {
            // Manager نمی‌تواند Admin بسازد/ویرایش کند
            var roles = await _db.AppRoles.AsNoTracking()
                .OrderBy(r => r.Name)
                .ToListAsync();

            if (!IsAdmin())
                roles = roles.Where(r => !string.Equals(r.Name, "Admin", StringComparison.OrdinalIgnoreCase)).ToList();

            return roles.Select(r => new SelectListItem
            {
                Value = r.Id.ToString(),
                Text = r.Name
            }).ToList();
        }

        public async Task<(bool ok, string? error, Guid? newUserId)> CreateAsync(UserCreateVm vm)
        {
            if (!IsAdmin() && !IsManager()) return (false, "اجازهٔ ایجاد کاربر را ندارید.", null);

            if (!vm.RoleId.HasValue) return (false, "نقش انتخابی نامعتبر است.", null);

            var role = await _db.AppRoles.FindAsync(vm.RoleId.Value);
            if (role == null) return (false, "נقش یافت نشد.", null);

            if (!IsAdmin() && string.Equals(role.Name, "Admin", StringComparison.OrdinalIgnoreCase))
                return (false, "ایجاد Admin فقط توسط ادمین مجاز است.", null);

            var usernameNorm = (vm.Username ?? "").Trim().ToLowerInvariant();
            var exists = await _db.AppUsers.AnyAsync(u => (u.Username ?? "").ToLower() == usernameNorm);
            if (exists) return (false, "نام کاربری تکراری است.", null);

            var pass = !string.IsNullOrWhiteSpace(vm.Password)
                ? vm.Password!
                : Guid.NewGuid().ToString("N")[..14] + "!";

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                Username = (vm.Username ?? "").Trim(),
                FullName = (vm.FullName ?? "").Trim(),
                RoleId = vm.RoleId.Value,
                IsActive = vm.IsActive,
                MustChangePassword = true,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(pass, workFactor: 12),
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.AppUsers.Add(user);
            await _db.SaveChangesAsync();
            return (true, null, user.Id);
        }

        public async Task<UserEditVm?> GetForEditAsync(Guid id)
        {
            var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return null;

            return new UserEditVm
            {
                Id = u.Id,
                Username = u.Username ?? "",
                FullName = u.FullName ?? "",
                RoleId = u.RoleId,
                IsActive = u.IsActive,
                MustChangePassword = u.MustChangePassword
            };
        }

        public async Task<(bool ok, string? error)> UpdateAsync(UserEditVm vm)
        {
            if (!vm.RoleId.HasValue)
                return (false, "نقش انتخابی نامعتبر است.");

            var user = await _db.AppUsers.FirstOrDefaultAsync(x => x.Id == vm.Id);
            if (user == null) return (false, "کاربر یافت نشد.");

            // نقش مقصد
            var newRole = await _db.AppRoles.FindAsync(vm.RoleId.Value);
            if (newRole == null) return (false, "نقش نامعتبر است.");

            // محدودیت‌ها
            var isAdminTarget = newRole.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase);
            if (!IsAdmin() && isAdminTarget)
                return (false, "تغییر به Admin فقط توسط ادمین مجاز است.");

            user.FullName = (vm.FullName ?? "").Trim();
            user.RoleId = vm.RoleId.Value;
            user.IsActive = vm.IsActive;
            user.MustChangePassword = vm.MustChangePassword;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync();
            return (true, null);
        }

        public async Task<ResetPasswordResultVm> ResetPasswordAsync(Guid id)
        {
            var selfId = CurrentUserId();
            if (selfId.HasValue && selfId.Value == id)
                return new ResetPasswordResultVm { Ok = false, Message = "نمی‌توانید گذرواژهٔ خودتان را از این مسیر ریست کنید." };

            var u = await _db.AppUsers.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return new ResetPasswordResultVm { Ok = false, Message = "کاربر یافت نشد." };

            var isAdminRow = u.Role?.Name?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;
            if (isAdminRow && !IsAdmin())
                return new ResetPasswordResultVm { Ok = false, Message = "ریست Admin فقط توسط ادمین مجاز است." };

            if (!IsAdmin() && !IsManager())
                return new ResetPasswordResultVm { Ok = false, Message = "اجازهٔ این عملیات را ندارید." };

            var newPass = Guid.NewGuid().ToString("N")[..16] + "!";
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPass, workFactor: 12);
            u.MustChangePassword = true;
            u.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            return new ResetPasswordResultVm { Ok = true, Message = "گذرواژه ریست شد.", NewPassword = newPass };
        }

        public async Task<(bool ok, string? error)> DeleteAsync(Guid id)
        {
            if (!IsAdmin()) return (false, "حذف فقط برای ادمین مجاز است.");

            var selfId = CurrentUserId();
            if (selfId.HasValue && selfId.Value == id)
                return (false, "حذف خودتان مجاز نیست.");

            var u = await _db.AppUsers.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id);
            if (u == null) return (false, "کاربر یافت نشد.");
            if (u.Role?.Name?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true)
                return (false, "حذف Admin مجاز نیست.");

            _db.AppUsers.Remove(u);
            await _db.SaveChangesAsync();
            return (true, null);
        }
    }
}
