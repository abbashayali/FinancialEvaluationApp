using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using UserAdminKit.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// MVC: Anti-Forgery + الزام لاگین برای همه صفحات (مگر [AllowAnonymous])
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add(new AuthorizeFilter());
});

// DbContext
builder.Services.AddDbContext<FinancialEvaluationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Cache و سرویس‌های برنامه
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ILookupService, LookupService>();

// سرویسی که کنترلر Users به آن نیاز دارد
builder.Services.AddScoped<IUserAdminService, EfUserAdminService>();

// برای سرویس‌ها/کنترلرهایی که به HttpContext نیاز دارند
builder.Services.AddHttpContextAccessor();

// Throttle لاگین
builder.Services.AddSingleton<ILoginThrottle, MemoryLoginThrottle>();

// DataProtection: ذخیره کلیدها در فولدر پروژه
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
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.ReturnUrlParameter = "returnUrl";
    });

// سیاست‌های دسترسی برای Users
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly",
        p => p.RequireAssertion(ctx =>
            ctx.User.IsInRole("Admin") || ctx.User.HasClaim("is_admin", "1")));

    options.AddPolicy("AdminOrManager",
        p => p.RequireAssertion(ctx =>
            ctx.User.IsInRole("Admin") || ctx.User.HasClaim("is_admin", "1") ||
            ctx.User.IsInRole("Manager") || ctx.User.HasClaim("is_manager", "1")));
});

var app = builder.Build();

// خطایابی/امنیت
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// --- CSP با nonce برای اسکریپت داخلی تم ---
app.Use(async (ctx, next) =>
{
    // nonce برای این درخواست
    var nonce = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    ctx.Items["CSP_NONCE"] = nonce;

    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

    // اجازه به اسکریپت‌های خود سایت + inline با nonce
    // استایل inline فعلاً مجاز است؛ فونت‌ها هم از خود سایت/دیتا URI
    ctx.Response.Headers["Content-Security-Policy"] =
        $"default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}'; " +
        $"style-src 'self' 'unsafe-inline'; " +
        $"img-src 'self' data:; " +
        $"font-src 'self' data:; " +
        $"connect-src 'self'; " +
        $"object-src 'none'; frame-ancestors 'none'; base-uri 'self';";

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// روت‌ها (Area ها قبل از پیش‌فرض)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
