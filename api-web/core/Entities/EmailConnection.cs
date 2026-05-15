using core.Enums;

namespace core.Entities;

public class EmailConnection : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Email { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public EmailConnectionStatus Status { get; set; } = EmailConnectionStatus.Active;
}
