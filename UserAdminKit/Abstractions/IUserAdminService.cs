using Microsoft.AspNetCore.Mvc.Rendering;

namespace UserAdminKit.Abstractions;

public interface IUserAdminService
{
    // لیست کاربران با سورت/جهت
    Task<IReadOnlyList<UserListItemVm>> GetUsersAsync(string sort, string dir);

    // نقش‌ها برای DropDown (بر اساس نقش کاربر جاری، Admin را فیلتر می‌کند)
    Task<IReadOnlyList<SelectListItem>> GetRoleOptionsAsync();

    // ساخت
    Task<(bool ok, string? error, Guid? newUserId)> CreateAsync(UserCreateVm vm);

    // ویرایش
    Task<UserEditVm?> GetForEditAsync(Guid id);
    Task<(bool ok, string? error)> UpdateAsync(UserEditVm vm);

    // ریست پسورد
    Task<ResetPasswordResultVm> ResetPasswordAsync(Guid id);

    // حذف
    Task<(bool ok, string? error)> DeleteAsync(Guid id);
}
