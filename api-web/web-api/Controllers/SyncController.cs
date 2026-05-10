using api_contracts.Requests;
using core.Entities;
using core.Enums;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public SyncController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpPost]
    public async Task<IActionResult> StartSync([FromBody] StartSyncRequest request)
    {
        var userExists = await _dbContext.Users.AnyAsync(u => u.Id == request.UserId);
        if (!userExists)
            return BadRequest(new { error = "User not found" });

        var job = new SyncJob
        {
            Id = Guid.NewGuid(),
            UserId = request.UserId,
            Status = SyncJobStatus.Pending
        };

        _dbContext.SyncJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        return Ok(new { jobId = job.Id });
    }

    [HttpGet("{jobId:guid}")]
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
