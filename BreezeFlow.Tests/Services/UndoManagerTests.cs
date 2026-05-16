using BreezeFlow.Services;
using Xunit;

namespace BreezeFlow.Tests.Services
{
    public class UndoManagerTests
    {
        [Fact]
        public void Execute_RunsDoOnce_AndPushesToUndo()
        {
            int doCount = 0, undoCount = 0;
            var m = new UndoManager();
            m.Execute("op", () => doCount++, () => undoCount++);

            Assert.Equal(1, doCount);
            Assert.Equal(0, undoCount);
            Assert.True(m.CanUndo);
            Assert.False(m.CanRedo);
            Assert.Equal("op", m.NextUndoDescription);
        }

        [Fact]
        public void Undo_RunsUndo_AndPopsToRedo()
        {
            int doCount = 0, undoCount = 0;
            var m = new UndoManager();
            m.Execute("op", () => doCount++, () => undoCount++);

            var c = m.Undo();
            Assert.NotNull(c);
            Assert.Equal(1, undoCount);
            Assert.False(m.CanUndo);
            Assert.True(m.CanRedo);
        }

        [Fact]
        public void Redo_RunsDoAgain()
        {
            int doCount = 0, undoCount = 0;
            var m = new UndoManager();
            m.Execute("op", () => doCount++, () => undoCount++);
            m.Undo();
            m.Redo();

            Assert.Equal(2, doCount);
            Assert.Equal(1, undoCount);
            Assert.True(m.CanUndo);
            Assert.False(m.CanRedo);
        }

        [Fact]
        public void ExecutingNewCommand_ClearsRedoStack()
        {
            var m = new UndoManager();
            m.Execute("a", () => { }, () => { });
            m.Undo();
            Assert.True(m.CanRedo);

            m.Execute("b", () => { }, () => { });
            Assert.False(m.CanRedo);
        }

        [Fact]
        public void Clear_EmptiesBothStacks()
        {
            var m = new UndoManager();
            m.Execute("a", () => { }, () => { });
            m.Execute("b", () => { }, () => { });
            m.Undo();

            m.Clear();
            Assert.False(m.CanUndo);
            Assert.False(m.CanRedo);
        }

        [Fact]
        public void Capacity_DropsOldestEntry()
        {
            var m = new UndoManager(capacity: 2);
            m.Execute("a", () => { }, () => { });
            m.Execute("b", () => { }, () => { });
            m.Execute("c", () => { }, () => { });

            // 一番古い "a" は捨てられる → 残り b と c
            Assert.True(m.CanUndo);
            Assert.Equal("c", m.NextUndoDescription);
            m.Undo();
            Assert.Equal("b", m.NextUndoDescription);
            m.Undo();
            Assert.False(m.CanUndo);
        }

        [Fact]
        public void Suppress_PreventsPushDuringScope()
        {
            var m = new UndoManager();
            using (m.Suppress())
            {
                m.Execute("inner", () => { }, () => { });
            }
            // Suppress 中に Execute された分は Do は走るが stack には積まれない
            Assert.False(m.CanUndo);
        }

        [Fact]
        public void ChangedEvent_FiresOnExecuteUndoRedoClear()
        {
            int fired = 0;
            var m = new UndoManager();
            m.Changed += () => fired++;

            m.Execute("a", () => { }, () => { });
            m.Undo();
            m.Redo();
            m.Clear();

            Assert.Equal(4, fired);
        }

        [Fact]
        public void NextUndoDescription_NullWhenEmpty()
        {
            var m = new UndoManager();
            Assert.Null(m.NextUndoDescription);
            Assert.Null(m.NextRedoDescription);
        }
    }
}
