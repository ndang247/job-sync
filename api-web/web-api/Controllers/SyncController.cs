using api_contracts.Requests;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;
using System.Globalization;

namespace web_api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sync")]
public class SyncController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ISyncJobChannel _syncJobChannel;

    public SyncController(AppDbContext dbContext, ISyncJobChannel syncJobChannel)
    {
        _dbContext = dbContext;
        _syncJobChannel = syncJobChannel;
    }

    [HttpPost]
    public async Task<IActionResult> StartSync(
        [FromBody] StartSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        if (!TryResolveSyncWindow(request, out var syncWindow, out var validationError))
            return BadRequest(validationError);

        var connection = await _dbContext.EmailConnections
            .Where(ec => ec.Id == request.EmailConnectionId && ec.UserId == userId)
            .Select(ec => new { ec.UserId, ec.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
            return NotFound();

        if (connection.Status != EmailConnectionStatus.Active)
            return Conflict(new { code = "CONNECTION_REQUIRES_GRANT", error = "Email connection requires grant/reconnect." });

        var hasActiveJob = await _dbContext.SyncJobs.AnyAsync(j =>
            j.EmailConnectionId == request.EmailConnectionId &&
            (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Processing),
            cancellationToken);
        if (hasActiveJob)
            return Conflict(new { error = "A sync job is already in progress for this email connection" });

        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            UserId = connection.UserId,
            EmailConnectionId = request.EmailConnectionId,
            SyncStartUtc = syncWindow.StartUtc,
            SyncEndUtcExclusive = syncWindow.EndUtcExclusive,
            SyncTimeZone = syncWindow.TimeZoneId,
            Status = SyncJobStatus.Pending
        };

        _dbContext.SyncJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _syncJobChannel.WriteAsync(job.Id, cancellationToken);

        return Ok(new { jobId = job.Id });
    }

    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(
            j => j.Id == jobId && j.UserId == userId,
            cancellationToken);
        if (job is null)
            return NotFound();

        return Ok(new
        {
            jobId = job.Id,
            status = job.Status.ToString().ToLowerInvariant(),
            progress = job.Progress,
            stage = job.Stage,
            result = job.Result,
            error = job.Error
        });
    }

    private static bool TryResolveSyncWindow(
        StartSyncRequest request,
        out ResolvedSyncWindow syncWindow,
        out object? validationError)
    {
        syncWindow = default;
        validationError = null;

        var timeZoneId = request.DateRange?.TimeZone;
        TimeZoneInfo timeZone;
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            timeZone = TimeZoneInfo.Local;
        }
        else
        {
            try
            {
                timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                validationError = InvalidSyncDateRange("Sync timezone is not recognized.");
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                validationError = InvalidSyncDateRange("Sync timezone is invalid.");
                return false;
            }
        }

        if (request.DateRange is null)
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
            syncWindow = ToSyncWindow(today, today, timeZone);
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.DateRange.StartDate))
        {
            validationError = InvalidSyncDateRange("Sync start date is required when dateRange is supplied.");
            return false;
        }

        if (!TryParseDate(request.DateRange.StartDate, out var startDate))
        {
            validationError = InvalidSyncDateRange("Sync start date must use yyyy-MM-dd format.");
            return false;
        }

        var endDate = startDate;
        if (!string.IsNullOrWhiteSpace(request.DateRange.EndDate))
        {
            if (!TryParseDate(request.DateRange.EndDate, out endDate))
            {
                validationError = InvalidSyncDateRange("Sync end date must use yyyy-MM-dd format.");
                return false;
            }
        }

        if (endDate < startDate)
        {
            validationError = InvalidSyncDateRange("Sync end date must be on or after sync start date.");
            return false;
        }

        syncWindow = ToSyncWindow(startDate, endDate, timeZone);
        return true;
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static ResolvedSyncWindow ToSyncWindow(DateOnly startDate, DateOnly endDate, TimeZoneInfo timeZone)
    {
        var localStart = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEndExclusive = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return new ResolvedSyncWindow(
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), DateTimeKind.Utc),
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localEndExclusive, timeZone), DateTimeKind.Utc),
            timeZone.Id);
    }

    private static object InvalidSyncDateRange(string error)
    {
        return new { code = "INVALID_SYNC_DATE_RANGE", error };
    }

    private readonly record struct ResolvedSyncWindow(
        DateTime StartUtc,
        DateTime EndUtcExclusive,
        string TimeZoneId);
}
