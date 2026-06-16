using api_contracts.Requests;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;

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
}
