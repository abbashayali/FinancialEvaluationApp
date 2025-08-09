using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinancialEvaluationApp.Controllers.Setup
{
    public class CurrencyController : Controller
    {
        private readonly FinancialEvaluationDbContext _context;

        public CurrencyController(FinancialEvaluationDbContext context)
        {
            _context = context;
        }

        // لیست همه ارزها
        public async Task<IActionResult> Index()
        {
            var list = await _context.Currencies
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return View(list);
        }
    }
}
