using System.Diagnostics;
using System.Net;
using System.Text;

namespace AgentQueue;

internal sealed class AgentQueueLock : IDisposable
{
    private readonly FileStream stream;
    private readonly string lockPath;

    private AgentQueueLock(FileStream stream, string lockPath)
    {
        this.stream = stream;
        this.lockPath = lockPath;
    }

    public static AgentQueueLock Acquire(AgentQueuePaths paths, string command, TimeSpan timeout)
    {
        Directory.CreateDirectory(paths.AgentLoopDirectory);
        string lockPath = paths.LockFile;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (true)
        {
            try
            {
                FileStream stream = new(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                WriteLockContent(stream, command);
                return new AgentQueueLock(stream, lockPath);
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                string existing = File.Exists(lockPath) ? File.ReadAllText(lockPath, Encoding.UTF8) : "unknown";
                throw new AgentQueueException(
                    $"Timed out waiting for {paths.ToDisplayPath(lockPath)}. Existing lock: {existing.Trim()}",
                    ExitCodes.LockTimeout);
            }
        }
    }

    public void Dispose()
    {
        stream.Dispose();
        try
        {
            File.Delete(lockPath);
        }
        catch (IOException)
        {
        }
    }

    private static void WriteLockContent(FileStream stream, string command)
    {
        using StreamWriter writer = new(stream, new UTF8Encoding(false), leaveOpen: true);
        writer.WriteLine("pid=" + Environment.ProcessId.ToStringInvariant());
        writer.WriteLine("host=" + Dns.GetHostName());
        writer.WriteLine("command=" + command);
        writer.WriteLine("created_at=" + DateTimeOffset.UtcNow.ToString("O"));
        writer.Flush();
        stream.Flush(true);
    }
}
