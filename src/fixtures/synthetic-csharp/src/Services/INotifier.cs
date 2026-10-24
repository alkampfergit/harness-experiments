using Acme.Orders.Domain.Events;

namespace Acme.Orders.Services;

public interface INotifier
{
    Task NotifyAsync(OrderPlaced placed, CancellationToken cancellationToken);
}
