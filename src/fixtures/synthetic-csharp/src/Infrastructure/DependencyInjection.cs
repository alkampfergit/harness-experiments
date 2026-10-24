namespace Acme.Orders.Extensions;

/// <summary>A stand-in for the DI container so that the fixture needs no package.</summary>
public interface IServiceCollection
{
    void Register(Type service, Type implementation);

    object? Resolve(Type service);
}

public static class ServiceCollectionRegistration
{
    public static IServiceCollection AddScoped<TService, TImplementation>(this IServiceCollection services)
        where TImplementation : TService
    {
        services.Register(typeof(TService), typeof(TImplementation));
        return services;
    }
}
