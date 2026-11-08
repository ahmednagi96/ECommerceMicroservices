using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Catalog.Infrastructure.Data;
namespace Catalog.API.Controllers;
[ApiController]
[Route("api/[controller]")]

public class ProductsController(CatalogDbContext dbContext) : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProductById(int id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return NotFound($"المنتج برقم {id} غير موجود في المعرض.");
        return Ok(product);
    }
}