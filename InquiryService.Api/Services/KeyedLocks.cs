using System.Collections.Concurrent;

namespace InquiryService.Api.Services;

/// <summary>
/// Async lock per string key. Used to make sure the same inquiry is only ever executed by one
/// caller at a time; other callers for the same key wait and then reuse the result.
/// </summary>
public sealed class KeyedLocks
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly object _gate = new();

    public async Task<IAsyncDisposable> LockAsync(string key, CancellationToken ct)
    {
        Entry entry;
        lock (_gate)
        {
            entry = _entries.GetOrAdd(key, static _ => new Entry());
            entry.Users++;
        }

        await entry.Semaphore.WaitAsync(ct);

        return new Releaser(this, key, entry);
    }

    private void Release(string key, Entry entry)
    {
        entry.Semaphore.Release();

        lock (_gate)
        {
            entry.Users--;
            if (entry.Users == 0)
                _entries.TryRemove(key, out _);
        }
    }

    private sealed class Entry
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public int Users;
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly KeyedLocks _owner;
        private readonly string _key;
        private readonly Entry _entry;

        public Releaser(KeyedLocks owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            _owner.Release(_key, _entry);
            return ValueTask.CompletedTask;
        }
    }
}