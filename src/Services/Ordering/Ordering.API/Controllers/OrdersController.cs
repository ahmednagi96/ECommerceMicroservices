using Microsoft.AspNetCore.Mvc;
using Ordering.Domain.Exceptions;
using Ordering.Domain.Orders;

namespace Ordering.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController(IOrderRepository orderRepository, ILogger<OrdersController> logger) : ControllerBase
{
    // POST api/orders/{id}/confirm
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, order => order.Confirm(), cancellationToken);

    // POST api/orders/{id}/cancel
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        ExecuteAsync(id, order => order.Cancel(), cancellationToken);

    // نفس الخطوات لكل أمر: هات الأوردر ← نفّذ قاعدة الدومين ← احفظ (والحفظ بيوزّع الأحداث 📢)
    private async Task<IActionResult> ExecuteAsync(Guid id, Action<Order> action, CancellationToken cancellationToken)
    {
        var order = await orderRepository.GetByIdAsync(new OrderId(id), cancellationToken);
        if (order is null)
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Order not found",
                detail: $"Order '{id}' was not found.");

        try
        {
            action(order); // القواعد جوه الـ Order
        }
        catch (DomainException ex)
        {
            // خرق لقاعدة بيزنس (مثلاً تأكيد أوردر ملغي) ← 409 مش 500
            logger.LogWarning("Domain rule violated on order {OrderId}: {Reason}", id, ex.Message);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Business rule violation",
                detail: ex.Message);
        }

        await orderRepository.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
