using api_contracts.Responses;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/applications")]
public class ApplicationsController : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IApplicationListCacheState _applicationListCacheState;

    public ApplicationsController(
        AppDbContext dbContext,
        IMemoryCache cache,
        IApplicationListCacheState applicationListCacheState)
    {
        _dbContext = dbContext;
        _cache = cache;
        _applicationListCacheState = applicationListCacheState;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var cacheKey = $"applications:v1:version:{_applicationListCacheState.Version}:page:{page}:pageSize:{pageSize}";
        var response = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var query = _dbContext.JobApplications.AsNoTracking();
            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

            var items = await query
                .OrderByDescending(ja => ja.CreatedAt)
                .ThenByDescending(ja => ja.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ja => new ApplicationListItemResponse(
                    ja.Id,
                    ja.CompanyName,
                    ja.JobRole,
                    ja.AppliedDate,
                    ja.Status.ToString(),
                    ja.EmailConnection.Email,
                    ja.CreatedAt))
                .ToListAsync(cancellationToken);

            return new ApplicationListResponse(
                items,
                page,
                pageSize,
                totalCount,
                totalPages,
                page > 1,
                page < totalPages);
        });

        return Ok(response);
    }
}
