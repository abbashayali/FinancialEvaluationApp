using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using UserAdminKit.Abstractions;

namespace UserAdminKit.Controllers
{
    [Area("UserAdmin")]
    [Authorize(Policy = "AdminOrManager")] // پالیسی در میزبان تعریف می‌شود
    public sealed class UsersController : Controller
    {
        private readonly IUserAdminService _svc;
        public UsersController(IUserAdminService svc) => _svc = svc;

        // --- Helpers (داخل کلاس) ---
        private static List<SelectListItem> EnsureList(IEnumerable<SelectListItem>? items)
            => items?.ToList() ?? new List<SelectListItem>();

        private static List<RoleOptionVm> ToRoleOptions(IEnumerable<SelectListItem> items)
        {
            var list = new List<RoleOptionVm>();
            foreach (var i in items)
            {
                if (!Guid.TryParse(i.Value, out var gid)) gid = Guid.Empty;
                list.Add(new RoleOptionVm { Id = gid, Name = i.Text ?? string.Empty });
            }
            return list;
        }
        // ----------------------------

        [HttpGet]
        public async Task<IActionResult> Index(string? sort = "username", string? dir = "asc")
        {
            var items = await _svc.GetUsersAsync(sort ?? "username", dir ?? "asc");
            ViewBag.Sort = sort ?? "username";
            ViewBag.Dir = dir ?? "asc";
            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // سرویس: IReadOnlyList<SelectListItem> / IEnumerable<SelectListItem>
            var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());

            var vm = new UserCreateVm
            {
                Roles = roleItems,             // برای asp-items
                RoleOptions = ToRoleOptions(roleItems) // اگر ویو RoleOptionVm بخواهد
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UserCreateVm vm)
        {
            if (!ModelState.IsValid)
            {
                var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());
                vm.Roles = roleItems;
                vm.RoleOptions = ToRoleOptions(roleItems);
                return View(vm);
            }

            var (ok, err, _) = await _svc.CreateAsync(vm);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, err ?? "خطا در ایجاد کاربر");
                var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());
                vm.Roles = roleItems;
                vm.RoleOptions = ToRoleOptions(roleItems);
                return View(vm);
            }

            TempData["Success"] = "کاربر با موفقیت ایجاد شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var vm = await _svc.GetForEditAsync(id);
            if (vm is null)
            {
                TempData["Error"] = "کاربر یافت نشد.";
                return RedirectToAction(nameof(Index));
            }

            var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());
            vm.Roles = roleItems;
            vm.RoleOptions = ToRoleOptions(roleItems);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserEditVm vm)
        {
            if (!ModelState.IsValid)
            {
                var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());
                vm.Roles = roleItems;
                vm.RoleOptions = ToRoleOptions(roleItems);
                return View(vm);
            }

            var (ok, err) = await _svc.UpdateAsync(vm);
            if (!ok)
            {
                ModelState.AddModelError(string.Empty, err ?? "خطا در ذخیره‌سازی");
                var roleItems = EnsureList(await _svc.GetRoleOptionsAsync());
                vm.Roles = roleItems;
                vm.RoleOptions = ToRoleOptions(roleItems);
                return View(vm);
            }

            TempData["Success"] = "اطلاعات کاربر به‌روزرسانی شد.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(Guid id)
        {
            var r = await _svc.ResetPasswordAsync(id);
            TempData[r.Ok ? "Success" : "Error"] = r.Message;
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var (ok, err) = await _svc.DeleteAsync(id);
            TempData[ok ? "Success" : "Error"] = ok ? "کاربر حذف شد." : (err ?? "حذف نامعتبر");
            return RedirectToAction(nameof(Index));
        }
    }
}
