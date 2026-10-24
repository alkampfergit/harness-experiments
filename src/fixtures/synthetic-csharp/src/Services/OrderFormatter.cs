using Acme.Orders.Domain;

namespace Acme.Orders.Services;

public static class OrderFormatter
{
    public static string Format(Order order)
    {
        return Format(order, "EUR");
    }

    public static string Format(Order order, string currency)
    {
        var total = order.Total();
        return $"{order.Id}: {total.Amount} {currency}";
    }
}
