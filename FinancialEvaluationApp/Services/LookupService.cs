using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Services
{
    public interface ILookupService
    {
        Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync();
        Task ClearCurrencyCacheAsync();
    }

    public class LookupService : ILookupService
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly IMemoryCache _cache;
        private const string CurrencyCacheKey = "Lookup.Currencies.Active";

        public LookupService(FinancialEvaluationDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public async Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync()
        {
            // اگر در کش هست، همان را بده
            if (_cache.TryGetValue(CurrencyCacheKey, out IReadOnlyList<Currency> cached))
                return cached;

            // در غیر این صورت از دیتابیس بگیر و در کش ذخیره کن
            var list = await _db.Currencies
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync();

            _cache.Set(CurrencyCacheKey, list, TimeSpan.FromMinutes(30));
            return list;
        }

        public Task ClearCurrencyCacheAsync()
        {
            _cache.Remove(CurrencyCacheKey);
            return Task.CompletedTask;
        }
    }
}
