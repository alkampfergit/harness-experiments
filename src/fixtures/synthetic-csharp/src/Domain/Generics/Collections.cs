namespace Acme.Orders.Domain.Generics;

public class Cache<TKey>
{
    public class Entry<TValue>
    {
        public TKey? Key { get; set; }

        public TValue? Value { get; set; }
    }
}

public class Index<TKey>
{
    public class Entry<TValue>
    {
        public TKey? Key { get; set; }

        public int Position { get; set; }
    }
}
