using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace FRFrame
{
    public sealed class EventManager : IManagerBase
    {
        public override int Priority => 0;

        public EventBus Bus { get; private set; }
        public AssetEventBridge Bridge { get; private set; }

        public override UniTask InitializeAsync()
        {
            Bus = new EventBus();
            Bridge = new AssetEventBridge(Bus);
            return UniTask.CompletedTask;
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0)
        {
            return Bus.Subscribe(handler, owner, priority);
        }

        public IDisposable SubscribeOnce<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0)
        {
            return Bus.SubscribeOnce(handler, owner, priority);
        }

        public IDisposable SubscribeAsync<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0)
        {
            return Bus.SubscribeAsync(handler, owner, priority);
        }

        public IDisposable SubscribeAsyncOnce<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0)
        {
            return Bus.SubscribeAsyncOnce(handler, owner, priority);
        }

        public int Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            return Bus.Unsubscribe(handler);
        }

        public int UnsubscribeAll(object owner)
        {
            return Bus.UnsubscribeAll(owner);
        }

        public void Publish<TEvent>(TEvent evt)
        {
            Bus.Publish(evt);
        }

        public UniTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
        {
            return Bus.PublishAsync(evt, cancellationToken);
        }

        public void Post<TEvent>(TEvent evt)
        {
            Bus.Post(evt);
        }

        public void Flush()
        {
            Bus.Flush();
        }

        public int Flush(int maxCount)
        {
            return Bus.Flush(maxCount);
        }

        public int ClearPostQueue()
        {
            return Bus.ClearPostQueue();
        }

        public int ClearPostQueue<TEvent>()
        {
            return Bus.ClearPostQueue<TEvent>();
        }

        public int ClearPostQueue(Type eventType)
        {
            return Bus.ClearPostQueue(eventType);
        }

        public int GetListenerCount<TEvent>()
        {
            return Bus.GetListenerCount<TEvent>();
        }

        public int GetPostQueueCount()
        {
            return Bus.GetPostQueueCount();
        }

        public EventBusSnapshot CreateSnapshot()
        {
            return Bus.CreateSnapshot();
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            int max = Bus.Options.MaxFlushCountPerFrame;
            if (max > 0)
                Bus.Flush(max);
            else
                Bus.Flush();
        }

        public void ClearAll()
        {
            Bridge.Clear();
            Bus.ClearAll();
        }
    }
}
