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
            if (_cache.TryGetValue(CurrencyCacheKey, out IReadOnlyList<Currency>? cached) && cached is not null)
                return cached;

            var list = await _db.Currencies
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .AsNoTracking()
                .ToListAsync();

            IReadOnlyList<Currency> result = list; // non-null
            _cache.Set(CurrencyCacheKey, result, TimeSpan.FromMinutes(30));
            return result;
        }


        public Task ClearCurrencyCacheAsync()
        {
            _cache.Remove(CurrencyCacheKey);
            return Task.CompletedTask;
        }
    }
}
