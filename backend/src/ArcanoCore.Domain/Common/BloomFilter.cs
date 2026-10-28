using System;

namespace ArcanoCore.Domain.Common;

public class BloomFilter<T>
{
    private readonly int _m;
    private readonly int _k;
    private readonly byte[] _bits;
    private readonly Func<T, (ulong Hash1, ulong Hash2)> _hashProvider;
    private readonly object _lock = new();

    public BloomFilter(int capacity, double falsePositiveRate, Func<T, (ulong, ulong)> hashProvider)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (falsePositiveRate <= 0 || falsePositiveRate >= 1) throw new ArgumentOutOfRangeException(nameof(falsePositiveRate));
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));

        _m = (int)Math.Ceiling(-capacity * Math.Log(falsePositiveRate) / (Math.Log(2) * Math.Log(2)));
        _k = (int)Math.Round((double)_m / capacity * Math.Log(2));
        if (_k < 1) _k = 1;

        _bits = new byte[(_m + 7) / 8];
    }

    public void Add(T item)
    {
        var (h1, h2) = _hashProvider(item);
        lock (_lock)
        {
            for (int i = 0; i < _k; i++)
            {
                ulong bitPosition = (h1 + (ulong)i * h2) % (ulong)_m;
                int byteIndex = (int)(bitPosition / 8);
                int bitIndex = (int)(bitPosition % 8);
                _bits[byteIndex] |= (byte)(1 << bitIndex);
            }
        }
    }

    public bool MightContain(T item)
    {
        var (h1, h2) = _hashProvider(item);
        lock (_lock)
        {
            for (int i = 0; i < _k; i++)
            {
                ulong bitPosition = (h1 + (ulong)i * h2) % (ulong)_m;
                int byteIndex = (int)(bitPosition / 8);
                int bitIndex = (int)(bitPosition % 8);
                if ((_bits[byteIndex] & (byte)(1 << bitIndex)) == 0)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
