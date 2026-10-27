namespace LouisaWifi.Windows;

// A connection may report several attempt_fail events before its final complete event.
// The display name is the SSID, so also track connection mode and start events.
internal sealed class PendingConnection(Guid adapter, string profile)
{
    private readonly TaskCompletionSource<uint> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int external;
    private int started;
    public Task<uint> Completion => completion.Task;
    public bool External => Volatile.Read(ref external) != 0;
    public bool Started => Volatile.Read(ref started) != 0;
    public void Accept(Guid id, uint code, string name, uint reason, uint mode = 1)
    {
        if (id != adapter || External) return;
        var matches = name == profile && mode == 1; // temporary profile, not a saved profile
        if (code == 9)
        {
            if (!matches || Interlocked.Exchange(ref started, 1) != 0)
            {
                Interlocked.Exchange(ref external, 1);
                completion.TrySetResult(uint.MaxValue);
            }
        }
        if (code == 10 && matches && Started) completion.TrySetResult(reason);
    }
}
