using core.Entities;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace infrastructure.Services;

public class JobApplicationService : IJobApplicationService
{
    private readonly AppDbContext _dbContext;
    private readonly IApplicationListCacheState _applicationListCacheState;

    public JobApplicationService(AppDbContext dbContext, IApplicationListCacheState applicationListCacheState)
    {
        _dbContext = dbContext;
        _applicationListCacheState = applicationListCacheState;
    }

    public async Task AddApplicationsAsync(Guid emailConnectionId, List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications.Count == 0)
            return;

        var userId = await _dbContext.EmailConnections
            .Where(connection => connection.Id == emailConnectionId)
            .Select(connection => connection.UserId)
            .SingleAsync(cancellationToken);

        var incomingMessageIds = applications
            .Select(app => app.MessageId)
            .Distinct()
            .ToList();

        var existingMessageIds = await _dbContext.JobApplications
            .IgnoreQueryFilters()
            .Where(ja =>
                ja.EmailConnectionId == emailConnectionId &&
                incomingMessageIds.Contains(ja.MessageId))
            .Select(ja => ja.MessageId)
            .ToHashSetAsync(cancellationToken);

        var added = false;
        foreach (var app in applications)
        {
            if (existingMessageIds.Contains(app.MessageId))
                continue;
            app.UserId = userId;
            app.EmailConnectionId = emailConnectionId;
            _dbContext.JobApplications.Add(app);
            added = true;
        }

        if (!added)
            return;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _applicationListCacheState.Invalidate();
    }

    public async Task<bool> DeleteApplicationAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var application = await _dbContext.JobApplications
            .FirstOrDefaultAsync(
                ja => ja.Id == id && ja.UserId == userId,
                cancellationToken);

        if (application is null)
            return false;

        application.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _applicationListCacheState.Invalidate();

        return true;
    }
}
