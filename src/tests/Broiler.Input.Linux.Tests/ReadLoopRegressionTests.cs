using System;
using System.Threading;
using System.Threading.Tasks;

namespace Broiler.Input.Linux.Tests;

internal static class ReadLoopRegressionTests
{
    public static async Task ImmediateStop()
    {
        LinuxReadLoopSession session = new();
        using ManualResetEventSlim release = new();
        bool cancelled = false;
        session.Start(token =>
        {
            release.Wait();
            cancelled = token.IsCancellationRequested;
        }, default);
        Task stopping = session.StopAsync().AsTask();
        release.Set();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        if (!cancelled) throw new InvalidOperationException("Immediate stop lost the read-loop token.");
    }

    public static async Task StopFromCallback()
    {
        LinuxReadLoopSession session = new();
        TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Start(token =>
        {
            session.StopAsync().GetAwaiter().GetResult();
            if (!token.IsCancellationRequested)
                throw new InvalidOperationException("Callback stop did not request cancellation.");
            returned.SetResult();
        }, default);
        await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        // Once shutdown has completed the same helper can own a new read loop.
        session.Start(_ => { }, default);
        await session.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
}
