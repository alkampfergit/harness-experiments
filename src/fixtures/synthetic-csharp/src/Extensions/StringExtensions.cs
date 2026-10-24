namespace Acme.Orders.Extensions;

public static class StringExtensions
{
    public static string ToSlug(this string value)
    {
        return value.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
