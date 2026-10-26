using Acme.Orders.Domain;
using Acme.Orders.Extensions;
using Acme.Orders.Services;

namespace Acme.Orders;

public static class Program
{
    public static async Task RunAsync(IServiceCollection services, CancellationToken cancellationToken)
    {
        services.AddOrders();

        var service = (IOrderService)services.Resolve(typeof(IOrderService))!;
        var order = new Order(Guid.NewGuid());
        order.AddLine(new OrderLine("SKU-1", 2, new Money(9.5m, "EUR")));

        await service.PlaceOrderAsync(order, cancellationToken);
        Console.WriteLine(OrderFormatter.Format(order));
        Console.WriteLine("Spring Sale".ToSlug());
    }
}
