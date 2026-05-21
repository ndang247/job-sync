using core.Entities;

namespace core.Interfaces;

public interface IJobApplicationService
{
    Task AddApplicationsAsync(Guid emailConnectionId, List<JobApplication> applications, CancellationToken cancellationToken = default);
}
