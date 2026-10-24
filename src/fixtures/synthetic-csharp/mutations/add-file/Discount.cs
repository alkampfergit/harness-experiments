namespace Acme.Orders.Domain;

public sealed class Discount
{
    public Discount(decimal percent)
    {
        Percent = percent;
    }

    public decimal Percent { get; }

    public Money Apply(Money price) => new(price.Amount * (1m - Percent / 100m), price.Currency);
}
