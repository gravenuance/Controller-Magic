namespace ControllerMagic;

// Owned by the thread that acquired it and released on Dispose, so a relaunch started after
// Dispose (Restart) finds the name free instead of quietly exiting as a second instance.
internal sealed class SingleInstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstanceLock(Mutex mutex) => _mutex = mutex;

    public static SingleInstanceLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        if (createdNew)
            return new SingleInstanceLock(mutex);

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
