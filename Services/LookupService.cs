using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using FinancialEvaluationApp.Data;
using FinancialEvaluationApp.Models.RefData;

namespace FinancialEvaluationApp.Services
{
    public interface ILookupService
    {
        Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync();
    }

    public class LookupService : ILookupService
    {
        private readonly FinancialEvaluationDbContext _db;
        private readonly IMemoryCache _cache;
        private const string CurrencyCacheKey = ""Lookup.Currencies.Active"";

        public LookupService(FinancialEvaluationDbContext db, IMemoryCache cache)
        {
            _db = db; _cache = cache;
        }

        public async Task<IReadOnlyList<Currency>> GetActiveCurrenciesAsync()
        {
            if (_cache.TryGetValue(CurrencyCacheKey, out IReadOnlyList<Currency> cached))
                return cached;

            var list = await _db.Currencies
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
                .AsNoTracking().ToListAsync();

            _cache.Set(CurrencyCacheKey, list, TimeSpan.FromMinutes(30));
            return list;
        }
    }
}
