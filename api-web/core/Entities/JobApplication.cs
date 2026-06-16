using core.Enums;

namespace core.Entities;

public class JobApplication : BaseEntity
{
    public string MessageId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string AppliedDate { get; set; } = string.Empty;
    public JobApplicationStatus Status { get; set; } = JobApplicationStatus.Applied;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid EmailConnectionId { get; set; }
    public EmailConnection EmailConnection { get; set; } = null!;
}
