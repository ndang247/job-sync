using core.Enums;

namespace core.Models;

public sealed class JobApplication
{
    public string MessageId { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string AppliedDate { get; set; } = string.Empty;
    // Default to "applied"
    public string Status { get; set; } = "applied";

    public Entities.JobApplication ToEntity() => new()
    {
        MessageId = MessageId,
        CompanyName = CompanyName,
        JobRole = JobRole,
        AppliedDate = AppliedDate,
        Status = Enum.TryParse<JobApplicationStatus>(
                Status.Replace(" ", string.Empty),
                ignoreCase: true,
                out var parsed)
            ? parsed
            : JobApplicationStatus.Applied
    };
}
