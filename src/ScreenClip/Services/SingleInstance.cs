using System.Diagnostics;
using static ScreenClip.Services.NativeMethods;

namespace ScreenClip.Services;

/// <summary>
/// One ScreenClip per Windows session. Starting it again (Start menu, a picture opened with ScreenClip) wakes the
/// running one with the arguments of the new start, then the new process quits. The wait costs no thread: the
/// thread pool watches the event. The running one answers from its interface thread: when it does not, it hangs, and
/// the new start offers to close it and take its place (asked by the author: a user's ScreenClip once “did not start”).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string MutexName = @"Local\ScreenClip.Instance" + TestInstance.Suffix;
    private static readonly string EventName = @"Local\ScreenClip.Activate" + TestInstance.Suffix;
    private static readonly string AnswerName = @"Local\ScreenClip.Answer" + TestInstance.Suffix;

    // The arguments of the new start, handed over through a small file (a second start is rare and never concurrent).
    private static readonly string ArgumentsFile = Path.Combine(Path.GetTempPath(), $"ScreenClip-activate{TestInstance.Suffix}.txt");

    // The running one's process, for a new start to close it when it hangs.
    private static readonly string ProcessFile = Path.Combine(Path.GetTempPath(), $"ScreenClip-process{TestInstance.Suffix}.txt");

    // Long enough for a start under load (signing in to Windows); a running ScreenClip answers in a few milliseconds.
    private static readonly TimeSpan AnswerTime = TimeSpan.FromSeconds(8);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _event;
    private readonly EventWaitHandle _answer;
    private readonly RegisteredWaitHandle _wait;

    private SingleInstance(Mutex mutex, Action<string[]> onActivated)
    {
        _mutex = mutex;
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _answer = new EventWaitHandle(false, EventResetMode.AutoReset, AnswerName);
        _wait = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onActivated(TakeArguments()), null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            File.WriteAllText(ProcessFile, Environment.ProcessId.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The instance if this process is the first one, or if it took the place of one that hangs; otherwise null,
    /// after waking the running one with <paramref name="args"/>. <paramref name="onActivated"/> runs on a
    /// thread-pool thread, and must call <see cref="Answer"/> from the interface thread.
    /// </summary>
    public static SingleInstance? TryStart(string[] args, Action<string[]> onActivated)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (created)
            return new SingleInstance(mutex, onActivated);

        if (WakeRunning(args) || !TakeOverHung(mutex))
        {
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, onActivated);
    }

    /// <summary>Tells a new start that the interface is alive: called from it, for each start.</summary>
    public void Answer() => _answer.Set();

    // True when the running one answered, or when nothing can tell (it is starting or closing, or an older version).
    private static bool WakeRunning(string[] args)
    {
        try
        {
            File.WriteAllLines(ArgumentsFile, args);
            using var answer = EventWaitHandle.OpenExisting(AnswerName);
            using (var running = EventWaitHandle.OpenExisting(EventName))
                running.Set();
            return answer.WaitOne(AnswerTime);
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // Asks, closes the one that hangs and takes its mutex. False: this start ends.
    private static bool TakeOverHung(Mutex mutex)
    {
        ErrorLog.Record(new TimeoutException("The ScreenClip already running did not answer a new start."), "startup");
        if (MessageBoxW(IntPtr.Zero, Strings.Get("InstanceNotAnswering"), "ScreenClip",
                MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2 | MB_TOPMOST) != IDYES)
            return false;
        if (!CloseRunning())
        {
            MessageBoxW(IntPtr.Zero, Strings.Get("InstanceNotClosed"), "ScreenClip", MB_ICONWARNING | MB_TOPMOST);
            return false;
        }
        try
        {
            File.Delete(ArgumentsFile); // what it was not given: this start opens its own
            return mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            return true; // its owner was closed: it is this one's now
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
    }

    private static bool CloseRunning()
    {
        try
        {
            if (!int.TryParse(File.ReadAllText(ProcessFile).Trim(), out int id))
                return false;
            using var running = Process.GetProcessById(id);
            using var current = Process.GetCurrentProcess();
            // Only a ScreenClip of this session: the file may name a process that ended long ago.
            if (running.ProcessName != current.ProcessName || running.SessionId != current.SessionId)
                return false;
            running.Kill();
            return running.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
        _answer.Dispose();
        _mutex.Dispose();
        try
        {
            File.Delete(ProcessFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
