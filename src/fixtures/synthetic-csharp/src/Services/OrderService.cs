using Acme.Orders.Domain;
using Acme.Orders.Domain.Events;
using Acme.Orders.Domain.Generics;

namespace Acme.Orders.Services;

public sealed class OrderService : IOrderService
{
    private readonly IRepository<Order> _repository;
    private readonly INotifier _notifier;

    public OrderService(IRepository<Order> repository, INotifier notifier)
    {
        _repository = repository;
        _notifier = notifier;
    }

    public async Task PlaceOrderAsync(Order order, CancellationToken cancellationToken)
    {
        order.MarkPlaced();
        var total = order.Total();
        if (total.Amount <= 0m)
        {
            throw new InvalidOperationException("An order must have a positive total.");
        }

        await _repository.SaveAsync(order.Id, order, cancellationToken);
        await _notifier.NotifyAsync(new OrderPlaced(order.Id, DateTimeOffset.UtcNow), cancellationToken);
    }

    public Task<Order?> GetOrderAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _repository.FindAsync(id, cancellationToken);
    }
}
