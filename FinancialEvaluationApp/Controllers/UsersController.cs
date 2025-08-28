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
