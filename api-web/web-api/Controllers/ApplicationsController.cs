using core.Entities;
using infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace web_api.Controllers;

[ApiController]
[Route("api/v1/applications")]
public class ApplicationsController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ApplicationsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var applications = await _dbContext.JobApplications
            .OrderByDescending(ja => ja.CreatedAt)
            .Select(ja => new
            {
                ja.Id,
                ja.CompanyName,
                ja.JobRole,
                ja.AppliedDate,
                Status = ja.Status.ToString(),
                ja.EmailConnection.Email,
                ja.CreatedAt
            })
            .ToListAsync();

        return Ok(applications);
    }
}
