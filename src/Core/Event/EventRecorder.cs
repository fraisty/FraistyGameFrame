using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public sealed class EventRecorder
    {
        private readonly List<EventRecord> _records = new List<EventRecord>();

        public bool IsRecording { get; private set; }

        public void Start()
        {
            IsRecording = true;
            _records.Clear();
        }

        public IReadOnlyList<EventRecord> Stop()
        {
            IsRecording = false;
            return _records;
        }

        public void Clear()
        {
            _records.Clear();
        }

        public void Record<TEvent>(TEvent evt)
        {
            if (!IsRecording)
                return;

            string json = string.Empty;
            try
            {
                json = JsonUtility.ToJson(evt);
            }
            catch (Exception e)
            {
                FRLog.WarningT("Event", $"Event record serialize failed. Event:{typeof(TEvent).FullName}, Error:{e.Message}");
            }

            _records.Add(new EventRecord
            {
                EventType = typeof(TEvent),
                EventTypeName = typeof(TEvent).FullName,
                Json = json,
                Time = Time.realtimeSinceStartup,
                Frame = Time.frameCount
            });
        }
    }
}
