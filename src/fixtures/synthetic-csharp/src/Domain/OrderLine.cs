namespace Acme.Orders.Domain;

public sealed record OrderLine(string Sku, int Quantity, Money UnitPrice)
{
    public Money LineTotal() => Money.Zero(UnitPrice.Currency).Add(UnitPrice.Amount * Quantity);
}
