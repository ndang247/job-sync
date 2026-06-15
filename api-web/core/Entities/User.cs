using core.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace core.Entities;

public class User : IdentityUser<Guid>, IAuditableEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public ICollection<EmailConnection> EmailConnections { get; set; } = [];
}
