using System;
using System.Collections.Generic;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class EventBusTests
    {
        private struct Ping { public int Value; }
        private struct Pong { public int Value; }

        private sealed class CountingLogger : ISimLogger
        {
            public int Errors;
            public LogLevel MinLevel => LogLevel.Trace;
            public void Log(LogLevel level, string message) { if (level == LogLevel.Error) Errors++; }
        }

        [Test]
        public void Publish_InvokesHandlersInSubscriptionOrder()
        {
            var bus = new EventBus();
            var log = new List<string>();
            bus.Subscribe<Ping>(e => log.Add("a" + e.Value));
            bus.Subscribe<Ping>(e => log.Add("b" + e.Value));
            bus.Subscribe<Ping>(e => log.Add("c" + e.Value));
            bus.Publish(new Ping { Value = 1 });
            bus.Publish(new Ping { Value = 2 });
            CollectionAssert.AreEqual(new[] { "a1", "b1", "c1", "a2", "b2", "c2" }, log);
            Assert.AreEqual(2, bus.PublishedCount);
            Assert.AreEqual(3, bus.HandlerCount<Ping>());
        }

        [Test]
        public void Publish_IsTypeSeparated_AndWorksWithoutHandlers()
        {
            var bus = new EventBus();
            int pings = 0;
            bus.Subscribe<Ping>(e => pings++);
            bus.Publish(new Pong { Value = 3 });
            Assert.AreEqual(0, pings);
            Assert.AreEqual(0, bus.HandlerCount<Pong>());
            Assert.AreEqual(1, bus.PublishedCount);
        }

        [Test]
        public void Unsubscribe_RemovesOnlyThatHandler()
        {
            var bus = new EventBus();
            var log = new List<int>();
            Action<Ping> a = e => log.Add(1);
            Action<Ping> b = e => log.Add(2);
            bus.Subscribe(a);
            bus.Subscribe(b);
            bus.Unsubscribe(a);
            bus.Unsubscribe(a); // no-op
            bus.Unsubscribe<Pong>(e => { }); // unknown type: no-op
            bus.Publish(new Ping());
            CollectionAssert.AreEqual(new[] { 2 }, log);
            Assert.AreEqual(1, bus.HandlerCount<Ping>());
        }

        [Test]
        public void Subscribe_SameHandlerTwice_RunsTwice_UnsubscribeRemovesOne()
        {
            var bus = new EventBus();
            int n = 0;
            Action<Ping> h = e => n++;
            bus.Subscribe(h);
            bus.Subscribe(h);
            bus.Publish(new Ping());
            Assert.AreEqual(2, n);
            bus.Unsubscribe(h);
            bus.Publish(new Ping());
            Assert.AreEqual(3, n);
        }

        [Test]
        public void Subscribe_Null_Throws()
        {
            var bus = new EventBus();
            Assert.Throws<ArgumentNullException>(() => bus.Subscribe<Ping>(null));
        }

        [Test]
        public void SubscribeDuringPublish_TakesEffectNextPublish()
        {
            var bus = new EventBus();
            int late = 0, early = 0;
            Action<Ping> lateHandler = e => late++;
            bus.Subscribe<Ping>(e => { early++; if (early == 1) bus.Subscribe(lateHandler); });
            bus.Publish(new Ping());
            Assert.AreEqual(0, late, "copy-on-write: new handler must not run in the current publish");
            bus.Publish(new Ping());
            Assert.AreEqual(1, late);
        }

        [Test]
        public void UnsubscribeDuringPublish_CurrentPublishStillCompletes()
        {
            var bus = new EventBus();
            var log = new List<string>();
            Action<Ping> second = null;
            Action<Ping> first = e => { log.Add("first"); bus.Unsubscribe(second); };
            second = e => log.Add("second");
            Action<Ping> third = e => log.Add("third");
            bus.Subscribe(first);
            bus.Subscribe(second);
            bus.Subscribe(third);
            bus.Publish(new Ping());
            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, log, "snapshot semantics");
            log.Clear();
            bus.Publish(new Ping());
            CollectionAssert.AreEqual(new[] { "first", "third" }, log);
        }

        [Test]
        public void SelfUnsubscribeDuringPublish_DoesNotSkipOthers()
        {
            var bus = new EventBus();
            var log = new List<int>();
            Action<Ping> once = null;
            once = e => { log.Add(1); bus.Unsubscribe(once); };
            bus.Subscribe(once);
            bus.Subscribe<Ping>(e => log.Add(2));
            bus.Publish(new Ping());
            bus.Publish(new Ping());
            CollectionAssert.AreEqual(new[] { 1, 2, 2 }, log);
        }

        [Test]
        public void ReentrantPublish_Works()
        {
            var bus = new EventBus();
            int pongs = 0;
            bus.Subscribe<Ping>(e => bus.Publish(new Pong { Value = e.Value }));
            bus.Subscribe<Pong>(e => pongs += e.Value);
            bus.Publish(new Ping { Value = 5 });
            Assert.AreEqual(5, pongs);
            Assert.AreEqual(2, bus.PublishedCount);
        }

        [Test]
        public void HandlerException_NonStrict_IsIsolatedAndLogged()
        {
            var logger = new CountingLogger();
            var bus = new EventBus(logger);
            var log = new List<int>();
            bus.Subscribe<Ping>(e => log.Add(1));
            bus.Subscribe<Ping>(e => { throw new InvalidOperationException("boom"); });
            bus.Subscribe<Ping>(e => log.Add(3));
            Assert.DoesNotThrow(() => bus.Publish(new Ping()));
            CollectionAssert.AreEqual(new[] { 1, 3 }, log);
            Assert.AreEqual(1, bus.HandlerFaults);
            Assert.AreEqual(1, logger.Errors);
        }

        [Test]
        public void HandlerException_Strict_Rethrows()
        {
            var bus = new EventBus { StrictMode = true };
            var log = new List<int>();
            bus.Subscribe<Ping>(e => log.Add(1));
            bus.Subscribe<Ping>(e => { throw new InvalidOperationException("boom"); });
            bus.Subscribe<Ping>(e => log.Add(3));
            var ex = Assert.Throws<InvalidOperationException>(() => bus.Publish(new Ping()));
            Assert.AreEqual("boom", ex.Message);
            CollectionAssert.AreEqual(new[] { 1 }, log);
            Assert.AreEqual(1, bus.HandlerFaults);
        }

        [Test]
        public void SetLogger_Null_FallsBackToNullLogger()
        {
            var bus = new EventBus();
            bus.SetLogger(null);
            bus.Subscribe<Ping>(e => { throw new Exception("x"); });
            Assert.DoesNotThrow(() => bus.Publish(new Ping()));
        }

        [Test]
        public void Publish_WithHandlers_DoesNotAllocate()
        {
            var bus = new EventBus();
            long sum = 0;
            bus.Subscribe<Ping>(e => sum += e.Value);
            bus.Subscribe<Ping>(e => sum -= 1);
            bus.Subscribe<Pong>(e => sum += 2);
            // Warm-up (JIT, dictionary, delegates).
            for (int i = 0; i < 1000; i++) { bus.Publish(new Ping { Value = i }); bus.Publish(new Pong()); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { bus.Publish(new Ping { Value = i }); bus.Publish(new Pong()); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(after - before, 256, "EventBus.Publish allocated " + (after - before) + " bytes over 20k publishes");
            Assert.AreNotEqual(0, sum);
        }

        [Test]
        public void Publish_WithoutHandlers_DoesNotAllocate()
        {
            var bus = new EventBus();
            for (int i = 0; i < 100; i++) bus.Publish(new Ping());
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) bus.Publish(new Ping());
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.LessOrEqual(after - before, 256);
        }
    }
}
