namespace Acme.Orders.Domain;

/// <summary>An order. The totals live in the second part of this partial class.</summary>
public partial class Order
{
    private readonly List<OrderLine> _lines = new();

    public Order(Guid id)
    {
        Id = id;
        Status = OrderStatus.Draft;
    }

    public Guid Id { get; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    public void AddLine(OrderLine line)
    {
        _lines.Add(line);
    }

    public void MarkPlaced()
    {
        Status = OrderStatus.Placed;
    }
}
