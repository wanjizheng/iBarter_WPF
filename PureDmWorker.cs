using System;
using System.Collections.Concurrent;
using System.Threading;

namespace iBarter {
    public sealed class PureDmWorkerUnavailableException : TimeoutException {
        public PureDmWorkerUnavailableException(string message) : base(message) { }
    }

    /// <summary>
    /// Owns the PureDM COM object and serialises all DM/CV calls on a
    /// single dedicated STA thread. Eliminates cross-thread COM races
    /// that were corrupting the DX hook during scan.
    ///
    /// Root cause the design addresses:
    ///   - The DM COM object is created on the WPF UI/STA thread.
    ///   - A DispatcherTimer on the UI thread was calling
    ///     DM.GetCursorPos + DM.GetClientSize every 10 ms (~200 calls/sec).
    ///   - The scan path was running DM.GetClientSize / DM.Capture in
    ///     Task.Run on arbitrary MTA thread-pool threads.
    ///   - lock(dm) only wrapped DM.Capture, so the timer's DM calls
    ///     could enter DM at the same time as Capture on another thread.
    ///   - COM objects created on an STA thread have undefined
    ///     behaviour when called from a different thread, especially
    ///     when the calls are interleaved. That is the "anchor.bmp
    ///     not found", intermittent OCR empty, and "scan poisoned"
    ///     class of failures.
    ///
    /// Design:
    ///   - One long-lived dedicated STA thread owns the DM COM object
    ///     for the entire lifetime of iBarter.
    ///   - All DM/CV calls are marshalled through a BlockingCollection
    ///     queue. They run strictly serially on the STA thread.
    ///   - Callers wait on a ManualResetEventSlim that the worker sets
    ///     after the call returns.
    ///   - The worker swallows call exceptions and lets the caller
    ///     re-throw via a captured-exception field, so a bad call
    ///     can't kill the worker.
    ///
    /// Lifecycle:
    ///   - PureDmWorker.Start() must be called once at app startup.
    ///   - DmAutomation must then be constructed through Call(...), because
    ///     its constructor immediately creates the dm.dmsoft COM instance.
    ///   - PureDmWorker.Stop() at app shutdown.
    ///   - Before Start() is called, Call() falls through to a direct
    ///     invocation (so a small number of startup-time calls from
    ///     before the worker is up still work).
    /// </summary>
    public static class PureDmWorker {
        private static Thread _thread;
        private static BlockingCollection<Action> _queue;
        private static ManualResetEventSlim _itemAvailable;
        private static int _poisoned;
        private static string _poisonReason = "";

        public static bool IsRunning => _thread != null && _thread.IsAlive;
        public static bool IsPoisoned => Volatile.Read(ref _poisoned) != 0;
        public static string PoisonReason => _poisonReason;
        // 2026-07-10: DmAutomation ctor can take >8s on first call (dm.dll
        // plugin init / registration handshake), which is legitimate, not a
        // hang. Bumped to 60s with the user's explicit approval - covers the
        // worst legit init while still firing on real DM/COM deadlocks.
        internal static int CallTimeoutMilliseconds { get; set; } = 60000;

        public static void Start() {
            if (_thread != null) return;
            _poisonReason = "";
            Interlocked.Exchange(ref _poisoned, 0);
            _queue = new BlockingCollection<Action>();
            _itemAvailable = new ManualResetEventSlim(false);
            _thread = new Thread(WorkerLoop) {
                IsBackground = true,
                Name = "PureDM-STA-Worker",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        public static void Stop() {
            if (_queue == null) return;
            try {
                _queue.CompleteAdding();
                if (_thread != null) {
                    _thread.Join(5000);
                }
            }
            catch {
                // best effort
            }
            _queue = null;
            _thread = null;
            if (_itemAvailable != null) {
                _itemAvailable.Dispose();
                _itemAvailable = null;
            }
        }

        private static void WorkerLoop() {
            try {
                foreach (var action in _queue.GetConsumingEnumerable()) {
                    try {
                        action();
                    }
                    catch {
                        // Swallow - the caller observes the exception
                        // via the captured field and re-throws.
                        // We must NEVER let an exception kill the
                        // worker thread - that would deadlock every
                        // future DM call site.
                    }
                }
            }
            catch {
                // GetConsumingEnumerable can throw on CompleteAdding;
                // that's the normal shutdown path.
            }
        }

        /// <summary>
        /// Queue an action on the STA worker and wait for it to finish.
        /// Falls through to a direct invocation if Start() was not
        /// yet called (early init / shutdown windows).
        ///
        /// 2026-07-10: bounded wait with timeout. If the worker or its
        /// underlying DM/COM call hangs (observed: PureDM.Capture blocking
        /// on a stale CaptureSession across scans), we time out and throw
        /// so the scanner can log + skip instead of locking the whole UI.
        /// Timeout is `CallTimeoutMilliseconds` (default 60s on 2026-07-10,
        /// was 8s but DmAutomation ctor can legitimately take >8s on first
        /// call). After a timeout the worker is marked "poisoned" so all
        /// future calls fast-fail instead of queueing forever behind the
        /// hung action.
        /// </summary>
        public static void Call(Action action) {
            ThrowIfPoisoned();
            if (ReferenceEquals(Thread.CurrentThread, _thread)) {
                action();
                return;
            }
            if (_queue == null || _thread == null || !_thread.IsAlive) {
                action();
                return;
            }
            Exception capturedEx = null;
            bool completed = false;
            using (var done = new ManualResetEventSlim(false)) {
                _queue.Add(() => {
                    try { action(); completed = true; }
                    catch (Exception ex) { capturedEx = ex; completed = true; }
                    finally { done.Set(); }
                });
                if (!done.Wait(CallTimeoutMilliseconds)) {
                    throw Poison(
                        "PureDmWorker.Call timed out after " + CallTimeoutMilliseconds
                        + "ms. The STA worker is now unavailable; restart iBarter."
                        + " The underlying synchronous DM/COM/native call may still be running.");
                }
            }
            if (capturedEx != null) throw capturedEx;
        }

        /// <summary>
        /// Queue a function on the STA worker, wait, and return the result.
        /// Falls through to a direct invocation if Start() was not yet
        /// called.
        /// </summary>
        public static T Call<T>(Func<T> func) {
            ThrowIfPoisoned();
            if (ReferenceEquals(Thread.CurrentThread, _thread)) {
                return func();
            }
            if (_queue == null || _thread == null || !_thread.IsAlive) {
                return func();
            }
            T result = default(T);
            Exception capturedEx = null;
            using (var done = new ManualResetEventSlim(false)) {
                _queue.Add(() => {
                    try { result = func(); }
                    catch (Exception ex) { capturedEx = ex; }
                    finally { done.Set(); }
                });
                if (!done.Wait(CallTimeoutMilliseconds)) {
                    throw Poison(
                        "PureDmWorker.Call<T> timed out after " + CallTimeoutMilliseconds
                        + "ms. The STA worker is now unavailable; restart iBarter."
                        + " The underlying synchronous DM/COM/native call may still be running.");
                }
            }
            if (capturedEx != null) throw capturedEx;
            return result;
        }

        private static void ThrowIfPoisoned() {
            if (IsPoisoned) {
                throw new PureDmWorkerUnavailableException(
                    string.IsNullOrWhiteSpace(_poisonReason)
                        ? "PureDM STA worker is unavailable; restart iBarter."
                        : _poisonReason);
            }
        }

        private static PureDmWorkerUnavailableException Poison(string reason) {
            _poisonReason = reason;
            Interlocked.Exchange(ref _poisoned, 1);
            return new PureDmWorkerUnavailableException(reason);
        }
    }
}
