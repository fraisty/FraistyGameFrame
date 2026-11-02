using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class EventBusOptions
    {
        public int MaxPostQueueCount = 0;
        public int MaxFlushCountPerFrame = 0;
        public EventPostOverflowPolicy OverflowPolicy = EventPostOverflowPolicy.WarningAndDropNewest;
        public EventExceptionPolicy ExceptionPolicy = EventExceptionPolicy.Throw;
    }

    public enum EventPostOverflowPolicy
    {
        WarningAndDropNewest,
        DropNewest,
        DropOldest,
        Throw
    }

    public enum EventExceptionPolicy
    {
        Throw,
        LogAndContinue,
        CollectAndThrow
    }

    public sealed class EventBusSnapshot
    {
        public int TotalListenerCount;
        public int EventTypeCount;
        public int OwnerCount;
        public int PostQueueCount;
        public IReadOnlyList<EventListenerSnapshot> Listeners;
        public IReadOnlyList<EventOwnerSnapshot> Owners;
    }

    public sealed class EventListenerSnapshot
    {
        public Type EventType;
        public int ListenerCount;
        public int OnceListenerCount;
        public int MaxPriority;
        public int MinPriority;
    }

    public sealed class EventOwnerSnapshot
    {
        public string OwnerName;
        public Type OwnerType;
        public int SubscriptionCount;
    }

    public sealed class EventDispatchException
    {
        public Type EventType;
        public object Owner;
        public Exception Exception;
    }

    public sealed class EventDispatchAggregateException : Exception
    {
        public IReadOnlyList<EventDispatchException> Exceptions { get; }

        public EventDispatchAggregateException(IReadOnlyList<EventDispatchException> exceptions)
            : base($"Event dispatch failed. Count:{exceptions?.Count ?? 0}")
        {
            Exceptions = exceptions ?? Array.Empty<EventDispatchException>();
        }
    }

    public sealed class GameEventAssetSnapshot
    {
        public string Name;
        public int ListenerCount;
        public int PostQueueCount;
        public int RaiseCount;
        public float LastRaiseTime;
    }

    public sealed class EventRecord
    {
        public Type EventType;
        public string EventTypeName;
        public string Json;
        public float Time;
        public int Frame;
    }
}
