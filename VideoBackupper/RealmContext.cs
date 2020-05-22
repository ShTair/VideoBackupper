using Realms;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShComp
{
    class RealmContext : SynchronizationContext, IDisposable
    {
        private CancellationTokenSource _cts;
        private SemaphoreSlim _sem;
        private Queue<(SendOrPostCallback, object)> _q;

        private RealmContext(RealmConfiguration configuration, Action<Realm> func)
        {
            _sem = new SemaphoreSlim(0);
            _q = new Queue<(SendOrPostCallback, object)>();
            _cts = new CancellationTokenSource();

            Task.Run(() =>
            {
                SetSynchronizationContext(this);
                var realm = Realm.GetInstance(configuration);
                func(realm);
                Run(_cts.Token);
            });
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            lock (_q) { _q.Enqueue((d, state)); }
            _sem.Release();
        }

        private void Run(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SendOrPostCallback d;
                    object state;
                    _sem.Wait(cancellationToken);
                    lock (_q) { (d, state) = _q.Dequeue(); }
                    d(state);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }

        public static async Task InvokeAsync(RealmConfiguration configuration, Func<Realm, Task> func)
        {
            var tcs = new TaskCompletionSource<bool>();
            async void InvokeFunction(Realm realm)
            {
                await func(realm);
                tcs.TrySetResult(true);
            }

            using (var context = new RealmContext(configuration, InvokeFunction))
            {
                await tcs.Task;
            }
        }

        public static async Task<T> InvokeAsync<T>(RealmConfiguration configuration, Func<Realm, Task<T>> func)
        {
            var tcs = new TaskCompletionSource<T>();
            async void InvokeFunction(Realm realm)
            {
                var result = await func(realm);
                tcs.TrySetResult(result);
            }

            using (var context = new RealmContext(configuration, InvokeFunction))
            {
                return await tcs.Task;
            }
        }

        // 同期バージョンはコンテキストを操作する必要がないため、
        // そのままやっちゃう

        public static Task InvokeAsync(RealmConfiguration configuration, Action<Realm> func)
        {
            return Task.Run(() =>
            {
                using (var realm = Realm.GetInstance(configuration))
                {
                    func(realm);
                }
            });
        }

        public static Task<T> InvokeAsync<T>(RealmConfiguration configuration, Func<Realm, T> func)
        {
            return Task.Run(() =>
            {
                using (var realm = Realm.GetInstance(configuration))
                {
                    return func(realm);
                }
            });
        }
    }
}
