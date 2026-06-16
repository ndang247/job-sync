using System.Globalization;
using api_contracts.Requests;
using api_contracts.Responses;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using web_api.Authentication;

namespace web_api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/applications")]
public class ApplicationsController : ControllerBase
{
    private const int DefaultPageSize = 10;
    private const int MaxPageSize = 100;
    private const string AppliedDateFormat = "dd-MM-yyyy";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IApplicationListCacheState _applicationListCacheState;
    private readonly IJobApplicationService _jobApplicationService;

    public ApplicationsController(
        AppDbContext dbContext,
        IMemoryCache cache,
        IApplicationListCacheState applicationListCacheState,
        IJobApplicationService jobApplicationService)
    {
        _dbContext = dbContext;
        _cache = cache;
        _applicationListCacheState = applicationListCacheState;
        _jobApplicationService = jobApplicationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var cacheKey = $"applications:v1:user:{userId}:version:{_applicationListCacheState.Version}:page:{page}:pageSize:{pageSize}";
        var response = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var query = _dbContext.JobApplications
                .AsNoTracking()
                .Where(ja => ja.UserId == userId);
            var totalCount = await query.CountAsync(cancellationToken);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

            var items = await query
                .OrderByDescending(ja => ja.CreatedAt)
                .ThenByDescending(ja => ja.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ja => new ApplicationListItemProjection(
                    ja.Id,
                    ja.CompanyName,
                    ja.JobRole,
                    ja.AppliedDate,
                    ja.Status,
                    ja.EmailConnection.Email,
                    ja.CreatedAt))
                .ToListAsync(cancellationToken);

            return new ApplicationListResponse(
                items.Select(ToResponse).ToList(),
                page,
                pageSize,
                totalCount,
                totalPages,
                page > 1,
                page < totalPages);
        });

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var application = await _dbContext.JobApplications
            .AsNoTracking()
            .Where(ja => ja.Id == id && ja.UserId == userId)
            .Select(ja => new ApplicationListItemProjection(
                ja.Id,
                ja.CompanyName,
                ja.JobRole,
                ja.AppliedDate,
                ja.Status,
                ja.EmailConnection.Email,
                ja.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (application is null)
            return NotFound();

        return Ok(ToResponse(application));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var application = await _dbContext.JobApplications
            .Include(ja => ja.EmailConnection)
            .FirstOrDefaultAsync(
                ja => ja.Id == id && ja.UserId == userId,
                cancellationToken);

        if (application is null)
            return NotFound();

        if (!TryValidateUpdate(request, out var status, out var problem))
            return BadRequest(new { error = problem });

        application.CompanyName = request.CompanyName.Trim();
        application.JobRole = request.JobRole.Trim();
        application.Status = status;
        application.AppliedDate = request.AppliedDate.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        _applicationListCacheState.Invalidate();

        return Ok(ToResponse(new ApplicationListItemProjection(
            application.Id,
            application.CompanyName,
            application.JobRole,
            application.AppliedDate,
            application.Status,
            application.EmailConnection.Email,
            application.CreatedAt)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var deleted = await _jobApplicationService.DeleteApplicationAsync(
            userId,
            id,
            cancellationToken);

        return deleted ? NoContent() : NotFound();
    }

    private static bool TryValidateUpdate(
        UpdateApplicationRequest request,
        out JobApplicationStatus status,
        out string problem)
    {
        status = JobApplicationStatus.Applied;

        if (string.IsNullOrWhiteSpace(request.CompanyName))
        {
            problem = "Company name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.JobRole))
        {
            problem = "Job role is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.AppliedDate) ||
            !DateTime.TryParseExact(
                request.AppliedDate.Trim(),
                AppliedDateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            problem = "Applied date must use dd-MM-yyyy format.";
            return false;
        }

        if (!TryParseStatus(request.Status, out status))
        {
            problem = "Status is not supported.";
            return false;
        }

        problem = string.Empty;
        return true;
    }

    private static bool TryParseStatus(string value, out JobApplicationStatus status)
    {
        status = JobApplicationStatus.Applied;
        var normalized = value?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        status = normalized switch
        {
            "applied" => JobApplicationStatus.Applied,
            "interviewing" => JobApplicationStatus.Interviewing,
            "offered" => JobApplicationStatus.Offered,
            "companyrejected" => JobApplicationStatus.CompanyRejected,
            "candidaterejected" => JobApplicationStatus.CandidateRejected,
            _ => status
        };

        return normalized is "applied" or "interviewing" or "offered" or "companyrejected" or "candidaterejected";
    }

    private static ApplicationListItemResponse ToResponse(ApplicationListItemProjection application) => new(
        application.Id,
        application.CompanyName,
        application.JobRole,
        application.AppliedDate,
        ToDisplayStatus(application.Status),
        application.Email,
        application.CreatedAt);

    private static string ToDisplayStatus(JobApplicationStatus status) => status switch
    {
        JobApplicationStatus.Applied => "Applied",
        JobApplicationStatus.Interviewing => "Interviewing",
        JobApplicationStatus.Offered => "Offered",
        JobApplicationStatus.CompanyRejected => "Company Rejected",
        JobApplicationStatus.CandidateRejected => "Candidate Rejected",
        _ => "Applied"
    };

    private sealed record ApplicationListItemProjection(
        Guid Id,
        string CompanyName,
        string JobRole,
        string AppliedDate,
        JobApplicationStatus Status,
        string Email,
        DateTime CreatedAt);
}
