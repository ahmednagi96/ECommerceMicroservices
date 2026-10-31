using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Basket.Infrastructure.Data;
using Basket.Domain.Entities;

namespace Basket.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<User>>> Get()
    {
        var users = await dbContext.Users.ToListAsync();
        return Ok(users);
    }
}