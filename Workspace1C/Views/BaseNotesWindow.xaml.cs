#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Окно «Заметки базы» (п.8, 1.0.0.90): markdown-карточка (для кого база, контакты,
/// особенности обновления) с предпросмотром через <see cref="MarkdownRenderer"/> и список
/// секретов внешних сервисов. Значения секретов хранятся ТОЛЬКО в зашифрованном виде
/// (<see cref="NoteCrypto"/>: Windows — DPAPI, Linux — AES-ключ профиля); показ — по
/// требованию (кнопка «глаз»), в логах/выводе секреты не появляются (маскировка).
/// При недоступности ключа (перенос между ПК) поле показывает «зашифровано,
/// ключ недоступен» и не падает.
/// </summary>
public partial class BaseNotesWindow : Window
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly Infobase _infobase;
    private readonly string _infobaseKey;
    private readonly NoteStore _store;
    private readonly NoteCrypto _crypto;
    private readonly List<SecretRow> _secretRows = new();

    /// <summary>Строка секрета: имя + значение (ввод/показ по требованию) + удаление.</summary>
    private sealed class SecretRow
    {
        public TextBox NameBox { get; } = new()
        {
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        public PasswordBox PasswordBox { get; } = new() { Padding = new Thickness(6, 3, 6, 3) };

        public TextBox RevealBox { get; } = new()
        {
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        public Button RevealButton { get; } = new()
        {
            Content = "👁",
            Width = 30,
            Margin = new Thickness(6, 0, 0, 0)
        };

        public TextBlock StatusText { get; } = new()
        {
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        public Button RemoveButton { get; } = new() { Content = "✕", Width = 28 };

        public Panel Container { get; set; } = null!;

        /// <summary>Зашифрованное значение на диске (null — секрет добавлен сейчас).</summary>
        public string? StoredCipher { get; set; }

        /// <summary>Расшифрованное значение на момент загрузки (для сверки «не менялось»).</summary>
        public string? Plain { get; set; }

        public bool IsRevealed { get; set; }

        public string NameValue => NameBox.Text?.Trim() ?? string.Empty;

        public string ValueFromUi => IsRevealed ? (RevealBox.Text ?? string.Empty) : (PasswordBox.Password ?? string.Empty);
    }

    public BaseNotesWindow(Infobase infobase)
    {
        InitializeComponent();

        _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));
        _infobaseKey = InfobaseNote.KeyFor(infobase);
        _store = NoteStoreAccess.GetOrCreate();
        _crypto = NoteCrypto.CreateDefault();

        var title = string.Format(LocalizationManager.T("Notes.Title"), infobase.Name);
        Title = title;
        EditorTitle.Text = title;
        BaseSubtitle.Text = infobase.ServerDatabaseDisplay;

        var note = _store.Get(_infobaseKey);
        MarkdownBox.Text = note?.Markdown ?? string.Empty;
        if (note is not null)
        {
            foreach (var pair in note.Secrets)
                AddSecretRow(pair.Key, pair.Value);
        }

        // Фокус в поле заметки — отложенный вызов после показа окна (паттерн #299).
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => MarkdownBox.Focus()));
        };
    }

    private static Style? TryStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style;

    private void AddSecretRow(string name, string? storedCipher)
    {
        var row = new SecretRow();
        row.NameBox.Style = TryStyle("ModernTextBox");
        row.RevealBox.Style = TryStyle("ModernTextBox");
        row.RevealButton.Style = TryStyle("IconButton");
        row.RemoveButton.Style = TryStyle("IconButton");
        row.RemoveButton.ToolTip = LocalizationManager.T("Notes.DeleteSecret");

        row.NameBox.Text = name;

        if (!string.IsNullOrEmpty(storedCipher))
        {
            row.StoredCipher = storedCipher;
            var decrypted = _crypto.Decrypt(storedCipher);
            if (decrypted.Status == NoteCryptoStatus.Ok)
            {
                row.Plain = decrypted.Value;
                row.PasswordBox.Password = decrypted.Value ?? string.Empty;
            }
            else
            {
                // «Зашифровано, ключ недоступен»: значение не показываем, секрет на диске цел.
                row.PasswordBox.IsEnabled = false;
                row.RevealBox.IsEnabled = false;
                row.StatusText.Text = LocalizationManager.T(NoteCrypto.StatusTextKey(decrypted.Status));
            }
        }

        row.RevealButton.Click += (_, _) => ToggleReveal(row);
        row.RemoveButton.Click += (_, _) =>
        {
            if (row.Container is not null)
                SecretsPanel.Children.Remove(row.Container);
            _secretRows.Remove(row);
        };

        // Имя (слева), значение с кнопкой «глаз» (справа), кнопка удаления (край).
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var namePanel = new StackPanel();
        namePanel.Children.Add(row.NameBox);
        namePanel.Children.Add(row.StatusText);
        Grid.SetColumn(namePanel, 0);
        grid.Children.Add(namePanel);

        var valueCell = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        valueCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.PasswordBox.SetValue(Grid.ColumnProperty, 0);
        row.RevealBox.SetValue(Grid.ColumnProperty, 0);
        row.RevealButton.SetValue(Grid.ColumnProperty, 1);
        valueCell.Children.Add(row.PasswordBox);
        valueCell.Children.Add(row.RevealBox);
        valueCell.Children.Add(row.RevealButton);
        Grid.SetColumn(valueCell, 1);
        grid.Children.Add(valueCell);

        var removePanel = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        removePanel.Children.Add(row.RemoveButton);
        Grid.SetColumn(removePanel, 2);
        grid.Children.Add(removePanel);

        row.Container = grid;
        SecretsPanel.Children.Add(grid);
        _secretRows.Add(row);
    }

    private void ToggleReveal(SecretRow row)
    {
        if (row is null)
            return;

        if (row.IsRevealed)
        {
            row.IsRevealed = false;
            row.PasswordBox.Password = row.RevealBox.Text ?? string.Empty;
            row.RevealBox.Visibility = Visibility.Collapsed;
            row.PasswordBox.Visibility = Visibility.Visible;
            row.RevealButton.Content = "👁";
            return;
        }

        // Показ по требованию: расшифровываем значение здесь и сейчас.
        if (!string.IsNullOrEmpty(row.StoredCipher))
        {
            var decrypted = _crypto.Decrypt(row.StoredCipher);
            if (decrypted.Status != NoteCryptoStatus.Ok)
            {
                row.StatusText.Text = LocalizationManager.T(NoteCrypto.StatusTextKey(decrypted.Status));
                return;
            }
            row.Plain = decrypted.Value;
            row.PasswordBox.Password = decrypted.Value ?? string.Empty;
        }

        row.IsRevealed = true;
        row.RevealBox.Text = row.PasswordBox.Password ?? string.Empty;
        row.PasswordBox.Visibility = Visibility.Collapsed;
        row.RevealBox.Visibility = Visibility.Visible;
        row.RevealBox.Focus();
        row.RevealBox.CaretIndex = row.RevealBox.Text.Length;
        row.RevealButton.Content = "🙈";
    }

    private void OnPreviewToggled(object sender, RoutedEventArgs e)
    {
        var previewVisible = PreviewCheck.IsChecked == true;
        if (previewVisible)
            PreviewViewer.Document = MarkdownRenderer.ToFlowDocument(MarkdownBox.Text ?? string.Empty);
        PreviewViewer.Visibility = previewVisible ? Visibility.Visible : Visibility.Collapsed;
        MarkdownBox.Visibility = previewVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAddSecretClick(object sender, RoutedEventArgs e) => AddSecretRow(string.Empty, null);

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _secretRows)
        {
            if (row is null)
                continue;
            var name = row.NameValue;
            if (name.Length == 0)
                continue;

            var currentValue = row.ValueFromUi;
            var unchanged = row.StoredCipher is not null && !row.IsRevealed && row.Plain is not null
                && string.Equals(row.Plain, currentValue, StringComparison.Ordinal);
            if (unchanged)
            {
                // Значение не трогали — сохраняем прежний шифртекст без повторного шифрования.
                secrets[name] = row.StoredCipher!;
                continue;
            }

            var encrypted = _crypto.Encrypt(currentValue);
            if (encrypted.Status != NoteCryptoStatus.Ok)
            {
                _dialogs.ShowWarning(
                    string.Format(LocalizationManager.T("Notes.SecretEncryptError"),
                        name, LocalizationManager.T(NoteCrypto.StatusTextKey(encrypted.Status))),
                    LocalizationManager.T("Notes.Title"));
                return;
            }
            secrets[name] = encrypted.Value ?? string.Empty;
        }

        var note = new InfobaseNote
        {
            InfobaseId = _infobaseKey,
            Markdown = MarkdownBox.Text ?? string.Empty,
            Secrets = secrets,
            UpdatedAt = DateTime.UtcNow
        };
        _store.Save(note);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
#endif
