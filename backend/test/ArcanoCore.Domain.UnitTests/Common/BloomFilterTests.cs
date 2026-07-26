using System;
using System.Collections.Generic;
using Xunit;
using ArcanoCore.Domain.Common;

namespace ArcanoCore.Domain.UnitTests.Common;

public class BloomFilterTests
{
    private static (ulong, ulong) GuidHashProvider(Guid guid)
    {
        byte[] bytes = guid.ToByteArray();
        return (BitConverter.ToUInt64(bytes, 0), BitConverter.ToUInt64(bytes, 8));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenArgumentsAreInvalid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter<Guid>(0, 0.01, GuidHashProvider));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter<Guid>(100, 0.0, GuidHashProvider));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter<Guid>(100, 1.0, GuidHashProvider));
        Assert.Throws<ArgumentNullException>(() => new BloomFilter<Guid>(100, 0.01, null!));
    }

    [Fact]
    public void MightContain_ShouldReturnTrue_ForInsertedItems()
    {
        // Arrange
        var filter = new BloomFilter<Guid>(100, 0.01, GuidHashProvider);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Act
        filter.Add(id1);
        filter.Add(id2);

        // Assert
        Assert.True(filter.MightContain(id1));
        Assert.True(filter.MightContain(id2));
    }

    [Fact]
    public void MightContain_ShouldReturnFalse_ForNonInsertedItems()
    {
        // Arrange
        var filter = new BloomFilter<Guid>(100, 0.01, GuidHashProvider);
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Act
        filter.Add(id1);

        // Assert
        Assert.False(filter.MightContain(id2)); // Probability of false positive is tiny (1%), should be false
    }

    [Fact]
    public void MightContain_ShouldHaveExpectedFalsePositiveRate()
    {
        // Arrange
        int capacity = 1000;
        double requestedFalsePositiveRate = 0.01; // 1%
        var filter = new BloomFilter<Guid>(capacity, requestedFalsePositiveRate, GuidHashProvider);

        var inserted = new List<Guid>();
        for (int i = 0; i < capacity; i++)
        {
            var id = Guid.NewGuid();
            inserted.Add(id);
            filter.Add(id);
        }

        // Verify no false negatives
        foreach (var id in inserted)
        {
            Assert.True(filter.MightContain(id));
        }

        // Measure false positives
        int falsePositives = 0;
        int testCount = 10000;
        for (int i = 0; i < testCount; i++)
        {
            var id = Guid.NewGuid();
            if (filter.MightContain(id))
            {
                falsePositives++;
            }
        }

        double actualFalsePositiveRate = (double)falsePositives / testCount;

        // Assert actual false positive rate is close to or below requested rate (allowing a reasonable tolerance)
        Assert.True(actualFalsePositiveRate <= requestedFalsePositiveRate + 0.01,
            $"Actual false positive rate {actualFalsePositiveRate} exceeded target {requestedFalsePositiveRate}");
    }
}
