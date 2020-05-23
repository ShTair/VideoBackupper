using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShComp
{
    static class SemaphoreSlimExtensions
    {
        public static async Task LockAsync(this SemaphoreSlim sem, Action action)
        {
            await sem.WaitAsync();
            try { action(); }
            finally { sem.Release(); }
        }

        public static async Task<T> LockAsync<T>(this SemaphoreSlim sem, Func<T> action)
        {
            await sem.WaitAsync();
            try { return action(); }
            finally { sem.Release(); }
        }

        public static async Task LockAsync(this SemaphoreSlim sem, Func<Task> action)
        {
            await sem.WaitAsync();
            try { await action(); }
            finally { sem.Release(); }
        }

        public static async Task<T> LockAsync<T>(this SemaphoreSlim sem, Func<Task<T>> action)
        {
            await sem.WaitAsync();
            try { return await action(); }
            finally { sem.Release(); }
        }
    }
}
