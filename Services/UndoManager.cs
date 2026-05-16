using System;
using System.Collections.Generic;

namespace BreezeFlow.Services
{
    /// <summary>
    /// 取り消し / やり直し可能な操作の最小インターフェイス。
    /// <see cref="Do"/> は実行 (記録のみ・再実行両方で呼ばれる)、<see cref="Undo"/> はその逆操作。
    /// 操作の説明は UI のステータス表示・履歴ログに使う (例: "ステップ追加: DB Input")。
    /// </summary>
    public interface IUndoCommand
    {
        /// <summary>初回の実行、または Redo 時に呼ばれる。</summary>
        void Do();
        /// <summary>Undo 時に呼ばれる。<see cref="Do"/> と完全に対称な逆操作であること。</summary>
        void Undo();
        string Description { get; }
    }

    /// <summary>
    /// Undo / Redo スタックを管理する。
    /// 既存の MainViewModel 経由のあらゆる変更 (ステップ追加・接続作成・移動・設定変更...) を
    /// このマネージャ経由で実行することで、Ctrl+Z / Ctrl+Y による取り消しと再実行を可能にする。
    ///
    /// パイプラインを開く / 新規作成 / クリアは "リセット" 扱いで履歴を捨てる (大きな状態変化は Undo すると混乱の元)。
    /// </summary>
    public class UndoManager
    {
        private readonly Stack<IUndoCommand> _undo = new();
        private readonly Stack<IUndoCommand> _redo = new();
        private readonly int _capacity;
        /// <summary>Do / Undo / Redo の最中に再帰的に新コマンドが Push されないようガード。</summary>
        private bool _suppressing;

        public UndoManager(int capacity = 200)
        {
            _capacity = capacity;
        }

        public event Action? Changed;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public string? NextUndoDescription => _undo.Count > 0 ? _undo.Peek().Description : null;
        public string? NextRedoDescription => _redo.Count > 0 ? _redo.Peek().Description : null;

        /// <summary>新規操作を実行してスタックに積む。Redo 履歴はクリアされる。</summary>
        public void Execute(IUndoCommand cmd)
        {
            if (_suppressing) { cmd.Do(); return; }

            _suppressing = true;
            try
            {
                cmd.Do();
                _undo.Push(cmd);
                if (_undo.Count > _capacity)
                {
                    // 古いものから捨てるためいったん配列化して再構築
                    var arr = _undo.ToArray();
                    _undo.Clear();
                    for (int i = arr.Length - 2; i >= 0; i--) _undo.Push(arr[i]);
                }
                _redo.Clear();
            }
            finally
            {
                _suppressing = false;
            }
            Changed?.Invoke();
        }

        /// <summary>直前の操作を取り消す。</summary>
        public IUndoCommand? Undo()
        {
            if (_undo.Count == 0) return null;
            var cmd = _undo.Pop();
            _suppressing = true;
            try { cmd.Undo(); }
            finally { _suppressing = false; }
            _redo.Push(cmd);
            Changed?.Invoke();
            return cmd;
        }

        /// <summary>取り消した操作をもう一度実行する。</summary>
        public IUndoCommand? Redo()
        {
            if (_redo.Count == 0) return null;
            var cmd = _redo.Pop();
            _suppressing = true;
            try { cmd.Do(); }
            finally { _suppressing = false; }
            _undo.Push(cmd);
            Changed?.Invoke();
            return cmd;
        }

        /// <summary>履歴を全消去する (パイプラインを開く / 新規作成 / クリアの直後に呼ぶ)。</summary>
        public void Clear()
        {
            if (_undo.Count == 0 && _redo.Count == 0) return;
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// 内部処理 (例: 接続作成と同時に ViewModel コレクションへ追加) のように、
        /// Undo 履歴に積みたくない一時的な変更を行うブロック。
        /// </summary>
        public IDisposable Suppress()
        {
            var prev = _suppressing;
            _suppressing = true;
            return new SuppressScope(this, prev);
        }

        private sealed class SuppressScope : IDisposable
        {
            private readonly UndoManager _m;
            private readonly bool _prev;
            public SuppressScope(UndoManager m, bool prev) { _m = m; _prev = prev; }
            public void Dispose() { _m._suppressing = _prev; }
        }

        /// <summary>呼び出し側で簡潔に書くためのアダプタ。</summary>
        public void Execute(string description, Action doAction, Action undoAction)
            => Execute(new InlineCommand(description, doAction, undoAction));

        private sealed class InlineCommand : IUndoCommand
        {
            private readonly Action _do;
            private readonly Action _undo;
            public InlineCommand(string description, Action doAction, Action undoAction)
            {
                Description = description;
                _do = doAction;
                _undo = undoAction;
            }
            public void Do() => _do();
            public void Undo() => _undo();
            public string Description { get; }
        }
    }
}
