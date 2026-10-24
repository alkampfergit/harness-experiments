namespace Acme.Orders.Domain.Events;

public record OrderPlaced(Guid OrderId, DateTimeOffset At);
