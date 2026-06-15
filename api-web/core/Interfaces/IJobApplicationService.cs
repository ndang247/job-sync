using core.Entities;

namespace core.Interfaces;

public interface IJobApplicationService
{
    Task AddApplicationsAsync(Guid emailConnectionId, List<JobApplication> applications, CancellationToken cancellationToken = default);
    Task<bool> DeleteApplicationAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken = default);
}
