using Acme.Orders.Domain;
using Acme.Orders.Domain.Generics;
using Acme.Orders.Services;

namespace Acme.Orders.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrders(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<INotifier, EmailNotifier>();
        services.AddScoped<IRepository<Order>, InMemoryRepository<Order>>();
        return services;
    }
}
