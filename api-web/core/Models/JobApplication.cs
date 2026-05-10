namespace core.Models;

public class JobApplication
{
    public string CompanyName { get; set; } = string.Empty;
    public string JobRole { get; set; } = string.Empty;
    public string AppliedDate { get; set; } = string.Empty;
    // Default status is "applied", but it can be updated to "interviewing", "offer", "rejected", etc.
    public string Status { get; set; } = "applied";
}
