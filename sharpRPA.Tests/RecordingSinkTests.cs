using System;
using sharpRPA.Core.Recording;
using Xunit;

namespace sharpRPA.Tests
{
    public class RecordingSinkTests
    {
        [Fact]
        public void InMemorySink_StoresRecordedActions()
        {
            var sink = new InMemoryRecordingSink();

            sink.Record(new RecordedAction { ActionType = "click", Target = "button" });
            sink.Record(new RecordedAction { ActionType = "type", Target = "input", Value = "hello" });

            Assert.Equal(2, sink.Actions.Count);
            Assert.Equal("click", sink.Actions[0].ActionType);
            Assert.Equal("button", sink.Actions[0].Target);
            Assert.Equal("hello", sink.Actions[1].Value);
            Assert.True(sink.Actions[0].Timestamp <= sink.Actions[1].Timestamp);
        }

        [Fact]
        public void InMemorySink_IgnoresNullActions()
        {
            var sink = new InMemoryRecordingSink();

            sink.Record(null);

            Assert.Empty(sink.Actions);
        }

        [Fact]
        public void NullSink_AcceptsAnyAction()
        {
            NullRecordingSink.Instance.Record(new RecordedAction { ActionType = "noop" });
            NullRecordingSink.Instance.Record(null);
        }
    }
}
