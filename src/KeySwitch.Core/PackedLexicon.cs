using System.Collections;
using System.Text;
namespace KeySwitch.Core;

/// <summary>Sorted UTF-8 pool; keeps 300k words without 300k managed string objects.</summary>
public sealed class PackedLexicon(byte[] pool, int[] offsets, int[] frequencies) : IReadOnlyDictionary<string, int>
{
    public int Count => offsets.Length - 1;
    public IEnumerable<string> Keys => Enumerable.Range(0, Count).Select(WordAt);
    public IEnumerable<int> Values => frequencies;
    public int this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
    private string WordAt(int index) => Encoding.UTF8.GetString(pool, offsets[index], offsets[index + 1] - offsets[index]);
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    public bool TryGetValue(string key, out int value)
    {
        var normalized = key.ToLowerInvariant(); var size = Encoding.UTF8.GetByteCount(normalized);
        Span<byte> search = size <= 512 ? stackalloc byte[size] : new byte[size]; Encoding.UTF8.GetBytes(normalized, search);
        var left = 0; var right = Count - 1;
        while (left <= right)
        {
            var middle = left + (right - left) / 2;
            var comparison = search.SequenceCompareTo(pool.AsSpan(offsets[middle], offsets[middle + 1] - offsets[middle]));
            if (comparison == 0) { value = frequencies[middle]; return true; }
            if (comparison < 0) right = middle - 1; else left = middle + 1;
        }
        value = 0; return false;
    }
    public IEnumerable<string> MostFrequent(int count) => Enumerable.Range(0, Count).OrderByDescending(i => frequencies[i]).Take(count).Select(WordAt);
    public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
    { for (var i = 0; i < Count; i++) yield return new(WordAt(i), frequencies[i]); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public static PackedLexicon Read(BinaryReader reader)
    {
        var count = reader.ReadInt32(); var byteCount = reader.ReadInt32();
        if (count is < 0 or > 2_000_000 || byteCount is < 0 or > 100_000_000) throw new InvalidDataException("Invalid model size");
        var bytes = reader.ReadBytes(byteCount); if (bytes.Length != byteCount) throw new EndOfStreamException();
        var starts = new int[count + 1]; for (var i = 0; i <= count; i++) starts[i] = reader.ReadInt32();
        var values = new int[count]; for (var i = 0; i < count; i++) values[i] = reader.ReadInt32();
        return new(bytes, starts, values);
    }
}
