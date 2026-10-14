namespace DlsiteClient.Core.Services;

/// <summary>Semaphore-like gate whose max concurrency can change at runtime.</summary>
internal sealed class ConcurrencyGate
{
    private readonly object _lock = new();
    private int _max;
    private int _inUse;
    private readonly Queue<TaskCompletionSource> _waiters = new();

    public ConcurrencyGate(int max) => _max = Math.Max(1, max);

    public void SetMax(int max)
    {
        lock (_lock)
        {
            _max = Math.Max(1, max);
            PulseUnlocked();
        }
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        TaskCompletionSource? tcs = null;
        lock (_lock)
        {
            if (_inUse < _max)
            {
                _inUse++;
                return;
            }

            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue(tcs);
        }

        await using (ct.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs))
            await tcs!.Task.ConfigureAwait(false);
    }

    public void Release()
    {
        lock (_lock)
        {
            _inUse = Math.Max(0, _inUse - 1);
            PulseUnlocked();
        }
    }

    private void PulseUnlocked()
    {
        while (_inUse < _max && _waiters.Count > 0)
        {
            var w = _waiters.Dequeue();
            if (w.TrySetResult())
                _inUse++;
        }
    }
}
