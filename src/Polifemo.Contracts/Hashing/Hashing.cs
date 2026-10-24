using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Polifemo.Contracts.Hashing;

/// <summary>A hash input: text (UTF-8) or raw bytes.</summary>
public readonly struct HashField
{
    internal ReadOnlyMemory<byte> Bytes { get; }

    private HashField(ReadOnlyMemory<byte> bytes) => Bytes = bytes;

    public static implicit operator HashField(string text) => new(Encoding.UTF8.GetBytes(text));

    public static implicit operator HashField(byte[] bytes) => new(bytes);

    public static HashField From(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());
}

/// <summary>
/// The canonical encoding of contracts/identities-and-hashes.md: every field is a 4-byte big-endian
/// length followed by its bytes, so no two different field lists share an encoding. Each hash starts
/// with a versioned tag (<c>lk/v1</c>, ...), so changing a rule means a new tag.
/// </summary>
public static class Hashing
{
    public static byte[] Encode(string tag, params HashField[] fields)
    {
        var all = new List<ReadOnlyMemory<byte>> { Encoding.UTF8.GetBytes(tag) };
        all.AddRange(fields.Select(f => f.Bytes));

        var buffer = new byte[all.Sum(f => 4 + f.Length)];
        var offset = 0;
        foreach (var field in all)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), (uint)field.Length);
            field.Span.CopyTo(buffer.AsSpan(offset + 4));
            offset += 4 + field.Length;
        }
        return buffer;
    }

    /// <summary>Lowercase hex SHA-256 of <see cref="Encode"/>.</summary>
    public static string Hash(string tag, params HashField[] fields) =>
        Convert.ToHexStringLower(SHA256.HashData(Encode(tag, fields)));
}
