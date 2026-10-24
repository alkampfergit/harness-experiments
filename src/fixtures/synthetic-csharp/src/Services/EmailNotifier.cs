using Acme.Orders.Domain.Events;

namespace Acme.Orders.Services;

public sealed class EmailNotifier : INotifier
{
    public Task NotifyAsync(OrderPlaced placed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
