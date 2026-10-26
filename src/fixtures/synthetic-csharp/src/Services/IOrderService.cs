using Acme.Orders.Domain;

namespace Acme.Orders.Services;

public interface IOrderService
{
    Task PlaceOrderAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default);
}
