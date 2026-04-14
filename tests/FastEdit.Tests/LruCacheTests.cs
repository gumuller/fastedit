using FastEdit.Core.HexEngine;

namespace FastEdit.Tests;

public class LruCacheTests
{
    [Fact]
    public void Add_And_TryGet_Returns_Value()
    {
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(1, value);
    }

    [Fact]
    public void TryGet_Missing_Key_Returns_False()
    {
        var cache = new LruCache<string, int>(3);

        Assert.False(cache.TryGet("missing", out _));
    }

    [Fact]
    public void Evicts_Least_Recently_Used_When_Over_Capacity()
    {
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);
        cache.Add("b", 2);
        cache.Add("c", 3);
        cache.Add("d", 4); // should evict "a"

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void Accessing_Item_Promotes_It()
    {
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);
        cache.Add("b", 2);
        cache.Add("c", 3);

        // Access "a" to promote it
        cache.TryGet("a", out _);

        cache.Add("d", 4); // should evict "b" (least recently used)

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void Clear_Removes_All_Entries()
    {
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);
        cache.Add("b", 2);
        cache.Clear();

        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void Add_Duplicate_Key_Updates_Value()
    {
        var cache = new LruCache<string, int>(3);
        cache.Add("a", 1);
        cache.Add("a", 99);

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal(99, value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_On_Invalid_Capacity(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(capacity));
    }

    [Fact]
    public void Concurrent_Access_Does_Not_Throw()
    {
        var cache = new LruCache<int, int>(100);
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            int threadId = t;
            tasks.Add(Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    cache.Add(threadId * 1000 + i, i);
                    cache.TryGet(threadId * 1000 + i, out _);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray()); // should not throw
    }
}
