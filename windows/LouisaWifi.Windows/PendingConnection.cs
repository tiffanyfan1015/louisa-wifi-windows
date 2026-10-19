namespace LouisaWifi.Windows;

// A connection may report several attempt_fail events before its final complete event.
// Only a complete event for this unique profile may finish the operation.
internal sealed class PendingConnection(Guid adapter, string profile)
{
    private readonly TaskCompletionSource<uint> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int external;
    public Task<uint> Completion => completion.Task;
    public bool External => Volatile.Read(ref external) != 0;
    public void Accept(Guid id, uint code, string name, uint reason)
    {
        if (id != adapter) return;
        if (code == 9 && name != profile)
        {
            Interlocked.Exchange(ref external, 1);
            completion.TrySetResult(uint.MaxValue);
        }
        if (code == 10 && name == profile) completion.TrySetResult(reason);
    }
}
