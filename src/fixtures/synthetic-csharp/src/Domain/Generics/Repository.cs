namespace Acme.Orders.Domain.Generics;

public interface IRepository<T>
{
    Task<T?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(Guid id, T item, CancellationToken cancellationToken);
}

public class InMemoryRepository<T> : IRepository<T>
{
    private readonly Dictionary<Guid, T> _items = new();

    public Task<T?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryGetValue(id, out var item) ? item : default);
    }

    public Task SaveAsync(Guid id, T item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _items[id] = item;
        return Task.CompletedTask;
    }
}
