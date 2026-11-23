using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>
    /// Synchronous, allocation-free typed event bus. Events are structs. Handlers are invoked in
    /// subscription order. A throwing handler is isolated (logged, not propagated) so a buggy
    /// UI/visual subscriber can never corrupt or halt the simulation, unless <see cref="StrictMode"/>.
    /// </summary>
    public sealed class EventBus
    {
        private interface IHandlerList { int Count { get; } }

        private sealed class HandlerList<T> : IHandlerList where T : struct
        {
            public Action<T>[] Handlers = new Action<T>[0];
            public int Count => Handlers.Length;
        }

        private readonly Dictionary<Type, IHandlerList> _lists = new Dictionary<Type, IHandlerList>();
        private ISimLogger _log;

        public EventBus(ISimLogger log = null) { _log = log ?? NullLogger.Instance; }

        public bool StrictMode { get; set; }
        public long PublishedCount { get; private set; }
        public int HandlerFaults { get; private set; }
        public void SetLogger(ISimLogger log) { _log = log ?? NullLogger.Instance; }

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            Guard.NotNull(handler, nameof(handler));
            var list = GetList<T>(true);
            var arr = new Action<T>[list.Handlers.Length + 1];
            Array.Copy(list.Handlers, arr, list.Handlers.Length);
            arr[arr.Length - 1] = handler;
            list.Handlers = arr; // copy-on-write: safe to (un)subscribe during publish
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var list = GetList<T>(false);
            if (list == null) return;
            int idx = Array.IndexOf(list.Handlers, handler);
            if (idx < 0) return;
            var arr = new Action<T>[list.Handlers.Length - 1];
            if (idx > 0) Array.Copy(list.Handlers, 0, arr, 0, idx);
            if (idx < list.Handlers.Length - 1) Array.Copy(list.Handlers, idx + 1, arr, idx, list.Handlers.Length - idx - 1);
            list.Handlers = arr;
        }

        public void Publish<T>(T evt) where T : struct
        {
            PublishedCount++;
            var list = GetList<T>(false);
            if (list == null) return;
            var handlers = list.Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](evt); }
                catch (Exception ex)
                {
                    HandlerFaults++;
                    _log.Error("Event handler for " + typeof(T).Name + " threw: " + ex);
                    if (StrictMode) throw;
                }
            }
        }

        public int HandlerCount<T>() where T : struct => GetList<T>(false)?.Count ?? 0;

        private HandlerList<T> GetList<T>(bool create) where T : struct
        {
            if (_lists.TryGetValue(typeof(T), out var l)) return (HandlerList<T>)l;
            if (!create) return null;
            var nl = new HandlerList<T>();
            _lists[typeof(T)] = nl;
            return nl;
        }
    }
}
