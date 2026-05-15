using System.Text.Json;
using core.Enums;

namespace core.Entities;

public class SyncJob : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid EmailConnectionId { get; set; }
    public EmailConnection EmailConnection { get; set; } = null!;
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;
    public int Progress { get; set; }
    public string? Stage { get; set; }
    public JsonDocument? Result { get; set; }
    public string? Error { get; set; }
}
