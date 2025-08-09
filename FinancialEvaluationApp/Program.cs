using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Services; 




var builder = WebApplication.CreateBuilder(args);

// 1) MVC//
builder.Services.AddControllersWithViews();

// 2) DbContext + ConnectionString از appsettings.json//
builder.Services.AddDbContext<FinancialEvaluationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3) Cache (برای Lookupها)//
builder.Services.AddMemoryCache();

// 4) سرویس‌های اختصاصی خودت (اختیاری)//
builder.Services.AddScoped<ILookupService, LookupService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
