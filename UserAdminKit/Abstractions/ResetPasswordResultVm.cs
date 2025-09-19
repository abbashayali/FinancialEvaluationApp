namespace UserAdminKit.Abstractions;

public sealed class ResetPasswordResultVm
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string? NewPassword { get; set; } // اگر لازم داری نمایش بدهی
}
