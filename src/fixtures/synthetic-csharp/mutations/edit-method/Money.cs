namespace Acme.Orders.Domain;

/// <summary>An amount of money in one currency.</summary>
public sealed class Money
{
    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other) => new(decimal.Round(Amount + other.Amount, 2), Currency);

    public Money Add(decimal amount) => new(Amount + amount, Currency);
}
