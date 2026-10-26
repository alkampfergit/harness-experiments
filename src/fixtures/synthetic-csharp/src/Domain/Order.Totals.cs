namespace Acme.Orders.Domain;

public partial class Order
{
    public Money Total()
    {
        var total = Money.Zero("EUR");
        foreach (var line in _lines)
        {
            total = total.Add(line.LineTotal());
        }

        return total;
    }
}
