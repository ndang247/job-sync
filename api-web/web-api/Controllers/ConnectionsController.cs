using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/connections")]
public class ConnectionsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ConnectionsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var connections = await _dbContext.EmailConnections
            .Where(ec => ec.DeletedAt == null)
            .Select(ec => new
            {
                ec.Id,
                ec.Email,
                Provider = ec.Provider.ToString(),
                Status = ec.Status.ToString(),
                ec.CreatedAt
            })
            .ToListAsync();

        return Ok(connections);
    }
}
