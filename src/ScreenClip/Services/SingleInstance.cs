namespace ScreenClip.Services;

/// <summary>
/// One ScreenClip per Windows session. Starting it again (Start menu, a picture opened with ScreenClip) wakes the
/// running one with the arguments of the new start, then the new process quits. The wait costs no thread: the
/// thread pool watches the event.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string MutexName = @"Local\ScreenClip.Instance" + TestInstance.Suffix;
    private static readonly string EventName = @"Local\ScreenClip.Activate" + TestInstance.Suffix;

    // The arguments of the new start, handed over through a small file (a second start is rare and never concurrent).
    private static readonly string ArgumentsFile = Path.Combine(Path.GetTempPath(), $"ScreenClip-activate{TestInstance.Suffix}.txt");

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _wait;

    private SingleInstance(Mutex mutex, Action<string[]> onActivated)
    {
        _mutex = mutex;
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onActivated(TakeArguments()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// The instance if this process is the first one; otherwise null, after waking the running one with
    /// <paramref name="args"/>. <paramref name="onActivated"/> runs on a thread-pool thread.
    /// </summary>
    public static SingleInstance? TryStart(string[] args, Action<string[]> onActivated)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (created)
            return new SingleInstance(mutex, onActivated);

        mutex.Dispose();
        try
        {
            File.WriteAllLines(ArgumentsFile, args);
            using var running = EventWaitHandle.OpenExisting(EventName);
            running.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or IOException or UnauthorizedAccessException)
        {
            // The other one is just starting or closing.
        }
        return null;
    }

    private static string[] TakeArguments()
    {
        try
        {
            if (!File.Exists(ArgumentsFile))
                return [];
            var args = File.ReadAllLines(ArgumentsFile);
            File.Delete(ArgumentsFile);
            return args;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _event.Dispose();
        _mutex.Dispose();
    }
}
