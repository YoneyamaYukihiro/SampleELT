using System.Windows.Controls;
using System.Windows.Input;

namespace BreezeFlow.Controls
{
    /// <summary>
    /// SQL 編集 TextBox 共通の Tab キー挙動。Tab キーで半角スペース 4 個を挿入する。
    /// Ctrl+Tab / Shift+Tab はそのまま (フォーカス移動・タブ文字挿入) に任せる。
    /// </summary>
    public static class SqlEditorBehavior
    {
        private const int TabSize = 4;

        public static void HandlePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) return;
            if (sender is not TextBox tb) return;

            // Ctrl+Tab → フォーカス移動 (WPF 既定挙動)
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;

            // Shift+Tab → タブ文字挿入の既定挙動に任せる
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;

            var spaces = new string(' ', TabSize);
            var start = tb.SelectionStart;
            var len   = tb.SelectionLength;

            tb.Text = tb.Text.Remove(start, len).Insert(start, spaces);
            tb.SelectionStart  = start + spaces.Length;
            tb.SelectionLength = 0;
            e.Handled = true;
        }
    }
}
