using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinancialEvaluationApp.Controllers.Setup
{
    public class FxRateController : Controller
    {
        private readonly FinancialEvaluationDbContext _context;

        public FxRateController(FinancialEvaluationDbContext context)
        {
            _context = context;
        }

        // GET: Setup/FxRate
        public async Task<IActionResult> Index()
        {
            var list = await _context.FxRates
                .Include(r => r.BaseCurrency)
                .Include(r => r.QuoteCurrency)
                .OrderByDescending(r => r.RateDate)
                .ToListAsync();

            return View(list);
        }

        // GET: Setup/FxRate/Create
        public IActionResult Create()
        {
            ViewBag.Currencies = _context.Currencies
                .OrderBy(c => c.Name)
                .ToList();
            return View();
        }

        // POST: Setup/FxRate/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FxRate fxRate)
        {
            if (ModelState.IsValid)
            {
                fxRate.Id = Guid.NewGuid();
                _context.Add(fxRate);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewBag.Currencies = _context.Currencies
                .OrderBy(c => c.Name)
                .ToList();
            return View(fxRate);
        }
    }
}
