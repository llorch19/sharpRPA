using System;
using System.Collections.Generic;

namespace sharpRPA.Core.Recording
{
    public sealed class RecordedAction
    {
        public string ActionType { get; set; }
        public string Target { get; set; }
        public string Value { get; set; }
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }

    public interface IRecordingSink
    {
        void Record(RecordedAction action);
    }

    public sealed class NullRecordingSink : IRecordingSink
    {
        public static readonly NullRecordingSink Instance = new NullRecordingSink();

        private NullRecordingSink()
        {
        }

        public void Record(RecordedAction action)
        {
        }
    }

    public sealed class InMemoryRecordingSink : IRecordingSink
    {
        private readonly List<RecordedAction> actions = new List<RecordedAction>();

        public IReadOnlyList<RecordedAction> Actions
        {
            get { return actions; }
        }

        public void Record(RecordedAction action)
        {
            if (action != null)
            {
                actions.Add(action);
            }
        }
    }
}
