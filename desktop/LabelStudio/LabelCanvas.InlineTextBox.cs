using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace LabelStudio;

public sealed partial class LabelCanvas
{
    sealed class InlineTextBox(LabelCanvas owner) : TextBox
    {
        protected override Type StyleKeyOverride => typeof(TextBox);

        bool selecting;
        int anchor;
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                base.OnPointerPressed(e);
                return;
            }

            Focus();
            var index = owner.TextHit(e.GetPosition(this));
            anchor = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SelectionStart : index;
            CaretIndex = index;
            SelectionStart = anchor;
            SelectionEnd = index;
            if (e.ClickCount == 2)
            {
                var text = Text ?? "";
                var left = index;
                var right = index;
                while (left > 0 && !char.IsWhiteSpace(text[left - 1]))
                    left--;
                while (right < text.Length && !char.IsWhiteSpace(text[right]))
                    right++;
                SelectionStart = anchor = left;
                SelectionEnd = right;
                CaretIndex = right;
            }

            selecting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!selecting)
            {
                base.OnPointerMoved(e);
                return;
            }

            var index = owner.TextHit(e.GetPosition(this));
            CaretIndex = index;
            SelectionStart = anchor;
            SelectionEnd = index;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!selecting)
            {
                base.OnPointerReleased(e);
                return;
            }

            selecting = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter && EditorShortcuts.HasCommand(e.KeyModifiers))
            {
                owner.EndTextEdit();
                owner.Focus();
                e.Handled = true;
                return;
            }

            if (EditorShortcuts.HasCommand(e.KeyModifiers) && e.Key is Key.Z or Key.Y)
            {
                owner.EndTextEdit();
                return;
            }

            if (!EditorShortcuts.HasCommand(e.KeyModifiers) && e.Key is Key.Up or Key.Down or Key.Home or Key.End && owner.editingObject != null)
            {
                using var geometry = new TextGeometry(owner.editingObject, owner.Document.Profile.DotsPerMm);
                var caret = geometry.Caret(CaretIndex);
                var point = e.Key == Key.Up || e.Key == Key.Down ? new Point(caret.X, caret.Y + caret.Height * (e.Key == Key.Up ? -.5 : 1.5)) : new Point(e.Key == Key.Home ? -100000 : 100000, caret.Y + caret.Height * .5);
                int index = geometry.Hit(point), start = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? SelectionStart : index;
                CaretIndex = index;
                SelectionStart = start;
                SelectionEnd = index;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }
    }

}
