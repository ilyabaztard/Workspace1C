#if WINDOWS
using System.Windows;
using System.Windows.Input;

namespace Workspace1C
{
    /// <summary>
    /// Базовый класс диалоговых окон WPF (issue #54): закрытие по Esc без модификаторов.
    /// Общее правило для всех новых диалоговых форм: в конструкторе подписывается
    /// <see cref="Window.PreviewKeyDown"/> — при Esc без модификаторов окно закрывается
    /// как «Отмена» (<see cref="Window.DialogResult"/> = false, <c>Close()</c>).
    /// Инвариант «сначала подсказка, потом окно» обеспечивает глобальный перехватчик
    /// <see cref="ToolTipCloser"/>: первый Esc с открытым тултипом помечает событие
    /// обработанным (этот обработчик на нём не сработает), повторный Esc уже закрывает
    /// окно. На Avalonia аналог общего правила — <see cref="ModalWindowBase"/>.
    /// </summary>
    public abstract class EscClosableWindow : Window
    {
        protected EscClosableWindow()
        {
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
                {
                    DialogResult = false;
                    Close();
                    e.Handled = true;
                }
            };
        }
    }
}
#endif