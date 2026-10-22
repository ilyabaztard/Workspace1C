using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using MaterialDesignThemes.Wpf;

namespace Workspace1C.Services;

/// <summary>
/// Всплывающее окно сообщения в стиле Material Design (Windows/WPF).
/// Используется <see cref="WpfDialogService"/> вместо стандартного MessageBox,
/// чтобы все предупреждения, подтверждения и ошибки выглядели единообразно.
/// </summary>
public partial class MaterialMessageWindow : Window
{
    /// <summary>Результат: true — пользователь подтвердил действие (OK/Да).</summary>
    public bool Confirmed { get; private set; } = true;

    public MaterialMessageWindow(string message, string title, MaterialMessageKind kind)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        // На старте главного окна ещё нет, и первым MainWindow становится само это
        // окно: присваивание Owner самому себе бросает ArgumentException. Без
        // владельца окно просто открывается по центру экрана.
        var owner = Application.Current?.MainWindow;
        if (owner is not null && !ReferenceEquals(owner, this))
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Настройка иконки и кнопок по типу сообщения.
        if (kind == MaterialMessageKind.Question)
        {
            MessageIcon.Kind = PackIconKind.HelpCircleOutline;
            // Текст кнопки «Да» задаём из менеджера локализации напрямую: SetResourceReference
            // искал бы статический ресурс "Common.Yes", которого в ResourceDictionary нет
            // (локализация идёт через LocalizationManager), и оставлял бы текст пустым —
            // кнопка выглядела бы полностью зелёной.
            OkText.Text = LocalizationManager.T("Common.Yes");
            CancelButton.Visibility = Visibility.Visible;
            // Обработчики кликов уже подключены в разметке через Click="OnOkClick"/"OnCancelClick".
        }
        else
        {
            Confirmed = true;
            MessageIcon.Kind = kind switch
            {
                MaterialMessageKind.Error => PackIconKind.AlertCircleOutline,
                MaterialMessageKind.Warning => PackIconKind.AlertOutline,
                _ => PackIconKind.InformationOutline
            };
        }

        Loaded += (_, _) => { OkButton.Focus(); };

        // ESC закрывает окно и для информационных сообщений (issue #47): у
        // подтверждений это делает кнопка «Нет» (IsCancel), а в информационных
        // режимах кнопка отмены скрыта (Visibility.Collapsed).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && CancelButton.Visibility != Visibility.Visible)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    /// <summary>
    /// Закрывает окно, безопасно устанавливая <see cref="Window.DialogResult"/>.
    /// DialogResult можно задавать только когда окно показано как модальный
    /// диалог через <see cref="Window.ShowDialog()"/>; иначе WPF бросает
    /// InvalidOperationException. Здесь присвоение обёрнуто в try/catch,
    /// а окно всегда закрывается корректно.
    /// </summary>
    private void CloseWithResult(bool result)
    {
        Confirmed = result;
        try
        {
            // Валидно только для модального окна (ShowDialog). Для немодального
            // пропускаем и просто закрываем окно через Close().
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // Окно не было показано как диалоговое — просто закрываем.
        }
        Close();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        CloseWithResult(true);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CloseWithResult(false);
    }
}