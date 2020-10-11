using System;
using System.Threading;
using System.Threading.Tasks;

namespace clausewitz_save_watcher
{
    /// <summary>
    /// Contains WaitHandle extesion methods.
    /// </summary>
    public static class WaitHandleExtension
    {
        /// <summary>
        /// Implements asyncronous WaitOne() with timeout.
        /// </summary>
        /// <param name="waitHandle">Specifies the type that the method operates on.</param>
        /// <param name="timeout">A TimeSpan that represents the number of milliseconds to wait, or a TimeSpan that represents -1 milliseconds to wait indefinitely.</param>
        /// <returns>First task.</returns>
        public static Task WaitOneAsync(this WaitHandle waitHandle, TimeSpan timeout)
        {
            if (waitHandle == null)
                throw new ArgumentNullException(nameof(waitHandle));

            long tm = (long)timeout.TotalMilliseconds;

            if (-1 > tm || (long)Int32.MaxValue < tm)
                throw new ArgumentOutOfRangeException(nameof(timeout));

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            RegisteredWaitHandle rwh = ThreadPool.RegisterWaitForSingleObject(waitHandle, delegate { tcs.TrySetResult(true); }, null, tm, true);
            Task t = tcs.Task;
            t.ContinueWith((antecedent) => rwh.Unregister(null));

            return t;
        }
    }
}
