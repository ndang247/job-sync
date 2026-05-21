using core.Entities;
using core.Interfaces;
using infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace infrastructure.Services;

public class JobApplicationService : IJobApplicationService
{
    private readonly AppDbContext _dbContext;

    public JobApplicationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddApplicationsAsync(Guid emailConnectionId, List<JobApplication> applications, CancellationToken cancellationToken = default)
    {
        if (applications.Count == 0)
            return;

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

        foreach (var app in applications)
        {
            if (existingMessageIds.Contains(app.MessageId))
                continue;
            app.EmailConnectionId = emailConnectionId;
            _dbContext.JobApplications.Add(app);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
