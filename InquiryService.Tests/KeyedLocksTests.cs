using InquiryService.Api.Services;

namespace InquiryService.Tests;

public sealed class KeyedLocksTests
{
    [Fact]
    public async Task Same_key_is_serialized()
    {
        var locks = new KeyedLocks();
        var gate = new object();
        var current = 0;
        var maxSeen = 0;

        var start = new TaskCompletionSource();

        async Task Work()
        {
            await start.Task;
            await using var handle = await locks.LockAsync("shared-key", CancellationToken.None);
            lock (gate)
            {
                current++;
                maxSeen = Math.Max(maxSeen, current);
            }
            await Task.Delay(200);
            lock (gate)
            {
                current--;
            }
        }

        var tasks = Enumerable.Range(0, 3).Select(_ => Task.Run(Work));
        start.SetResult();
        await Task.WhenAll(tasks);

        Assert.Equal(1, maxSeen);
    }

    [Fact]
    public async Task Different_keys_run_concurrently()
    {
        var locks = new KeyedLocks();
        var gate = new object();
        var current = 0;
        var maxSeen = 0;

        var start = new TaskCompletionSource();

        async Task Work(string key)
        {
            await start.Task;
            await using var handle = await locks.LockAsync(key, CancellationToken.None);
            lock (gate)
            {
                current++;
                maxSeen = Math.Max(maxSeen, current);
            }
            await Task.Delay(200);
            lock (gate)
            {
                current--;
            }
        }

        var tasks = new[] { "a", "b", "c" }.Select(k => Task.Run(() => Work(k)));
        start.SetResult();
        await Task.WhenAll(tasks);

        Assert.True(maxSeen >= 2, $"expected concurrent sections, maxSeen={maxSeen}");
    }
}