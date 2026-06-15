using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using web_api.Authentication;

namespace web_api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ConnectionsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var connections = await _dbContext.EmailConnections
            .Where(ec => ec.DeletedAt == null && ec.UserId == userId)
            .Select(ec => new
            {
                ec.Id,
                ec.Email,
                Provider = ec.Provider.ToString(),
                Status = ec.Status.ToString(),
                ec.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(connections);
    }
}
