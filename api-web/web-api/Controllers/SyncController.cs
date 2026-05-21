using api_contracts.Requests;
using core.Entities;
using core.Enums;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
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
    public async Task<IActionResult> StartSync([FromBody] StartSyncRequest request)
    {
        var connection = await _dbContext.EmailConnections
            .Where(ec => ec.Id == request.EmailConnectionId)
            .Select(ec => new { ec.UserId, ec.Status })
            .FirstOrDefaultAsync();

        if (connection is null || connection.UserId == Guid.Empty)
            return Conflict(new { code = "CONNECTION_REQUIRES_GRANT", error = "Email connection requires grant/reconnect." });

        if (connection.Status != EmailConnectionStatus.Active)
            return Conflict(new { code = "CONNECTION_REQUIRES_GRANT", error = "Email connection requires grant/reconnect." });

        var hasActiveJob = await _dbContext.SyncJobs.AnyAsync(j =>
            j.EmailConnectionId == request.EmailConnectionId &&
            (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Processing));
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
        await _dbContext.SaveChangesAsync();

        await _syncJobChannel.WriteAsync(job.Id);

        return Ok(new { jobId = job.Id });
    }

    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _dbContext.SyncJobs.FirstOrDefaultAsync(j => j.Id == jobId);
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
