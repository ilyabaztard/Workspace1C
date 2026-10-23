using System.Collections.ObjectModel;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Заменить в строках подключения…» (0.3.9.189, функция 6
/// «Массовая замена в строке подключения баз»): поля «Найти»/«Заменить на», выбор
/// поля/области/режима/регистра, предпросмотр-коллекция с «было → станет», сводка
/// (совпадений / будет изменено / без совпадений / без подключения), команды поиска,
/// применения и отмены последней замены, валидация регулярного выражения. Чистый .NET
/// без платформенных зависимостей — обе платформы (WPF и Avalonia); окна только
/// привязываются. Получает кандидатов и делегатов (сохранение undo-истории/пересборка
/// дерева выполняет <see cref="MainViewModel"/> через колбэки).
/// </summary>
public sealed class ConnectionReplaceViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Infobase> _candidates;
    private readonly Action<IReadOnlyList<ConnectionReplaceUndoEntry>>? _onApplied;
    private readonly Action? _onUndone;
    private readonly Action<Action>? _dispatchToUi;

    private string _findText = string.Empty;
    private string _replaceText = string.Empty;
    private ConnectionField _selectedField = ConnectionField.Server;
    private ConnectionReplaceScope _selectedScope;
    private ConnectionMatchMode _selectedMatchMode = ConnectionMatchMode.Substring;
    private bool _ignoreCase = true;
    private bool _changePlatform;
    private string _selectedPlatform = string.Empty;
    private bool _isPreviewDirty = true;
    private int _affectedCount;
    private int _noMatchCount;
    private int _emptyCount;
    private string _summaryText = string.Empty;
    private string _resultText = string.Empty;
    private bool _canApply;
    private bool _canUndo;
    private string _errorMessage = string.Empty;

    private ConnectionStringReplaceRule? _lastRule;
    private PlatformReplaceRule? _lastPlatformRule;
    private IReadOnlyList<ConnectionReplaceUndoEntry>? _lastEntries;

    private ICommand? _refreshPreviewCommand;
    private ICommand? _applyCommand;
    private ICommand? _undoLastCommand;

    /// <param name="candidates">Кандидаты области (видимые базы; приватные отфильтрованы вызывающей стороной).</param>
    /// <param name="initialScope">Начальная область применения (предзаполняется источником вызова).</param>
    /// <param name="onApplied">
    /// Колбэк после применения: <see cref="MainViewModel.ApplyConnectionReplace"/>
    /// (сохранение undo-истории, JSON-бэкап, персистентность, пересборка дерева).
    /// </param>
    /// <param name="onUndone">Колбэк после отката последней замены (сохранение в MainViewModel).</param>
    /// <param name="dispatchToUi">
    /// Доставка применения результатов в UI-поток (передаёт окно); null — результаты
    /// применяются прямо из вызывающего потока (тесты).
    /// </param>
    /// <param name="installedPlatformVersions">
    /// Установленные версии платформы 1С для ComboBox «Сменить платформу» (issue #54);
    /// null/пустой — выбор недоступен, галочка работает вхолостую.
    /// </param>
    public ConnectionReplaceViewModel(
        IReadOnlyList<Infobase> candidates,
        ConnectionReplaceScope initialScope,
        Action<IReadOnlyList<ConnectionReplaceUndoEntry>>? onApplied = null,
        Action? onUndone = null,
        Action<Action>? dispatchToUi = null,
        IReadOnlyList<string>? installedPlatformVersions = null)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _selectedScope = initialScope;
        _onApplied = onApplied;
        _onUndone = onUndone;
        _dispatchToUi = dispatchToUi;
        InstalledPlatformVersions = (installedPlatformVersions ?? Array.Empty<string>()).ToList();

        // Готовые списки для ComboBox'ов окна (0.3.9.190): значения enum + локализованные
        // тексты — единая точка, используются и WPF, и Avalonia.
        var batchCount = initialScope == ConnectionReplaceScope.BatchSelected ? candidates.Count : 0;
        Fields = new[]
        {
            new DisplayItem<ConnectionField>(ConnectionField.Server, FieldDisplayText(ConnectionField.Server)),
            new DisplayItem<ConnectionField>(ConnectionField.Port, FieldDisplayText(ConnectionField.Port)),
            new DisplayItem<ConnectionField>(ConnectionField.Ref, FieldDisplayText(ConnectionField.Ref)),
            new DisplayItem<ConnectionField>(ConnectionField.FilePath, FieldDisplayText(ConnectionField.FilePath)),
            new DisplayItem<ConnectionField>(ConnectionField.WebUrl, FieldDisplayText(ConnectionField.WebUrl)),
            new DisplayItem<ConnectionField>(ConnectionField.Any, FieldDisplayText(ConnectionField.Any))
        };
        Scopes = new[]
        {
            new DisplayItem<ConnectionReplaceScope>(ConnectionReplaceScope.AllBases, ScopeDisplayText(ConnectionReplaceScope.AllBases)),
            new DisplayItem<ConnectionReplaceScope>(ConnectionReplaceScope.BatchSelected, ScopeDisplayText(ConnectionReplaceScope.BatchSelected, batchCount)),
            new DisplayItem<ConnectionReplaceScope>(ConnectionReplaceScope.CurrentGroup, ScopeDisplayText(ConnectionReplaceScope.CurrentGroup))
        };
        Modes = new[]
        {
            new DisplayItem<ConnectionMatchMode>(ConnectionMatchMode.Exact, ModeDisplayText(ConnectionMatchMode.Exact)),
            new DisplayItem<ConnectionMatchMode>(ConnectionMatchMode.Prefix, ModeDisplayText(ConnectionMatchMode.Prefix)),
            new DisplayItem<ConnectionMatchMode>(ConnectionMatchMode.Substring, ModeDisplayText(ConnectionMatchMode.Substring)),
            new DisplayItem<ConnectionMatchMode>(ConnectionMatchMode.Regex, ModeDisplayText(ConnectionMatchMode.Regex))
        };
    }

    // ===================== Входные параметры =====================

    /// <summary>Искомый текст (пустой — правило не строится, предпросмотр пуст).</summary>
    public string FindText
    {
        get => _findText;
        set
        {
            if (SetProperty(ref _findText, value ?? string.Empty))
                MarkInputChanged();
        }
    }

    /// <summary>Текст замены (литеральный, без интерпретации $-ссылок regex).</summary>
    public string ReplaceText
    {
        get => _replaceText;
        set
        {
            if (SetProperty(ref _replaceText, value ?? string.Empty))
                MarkInputChanged();
        }
    }

    /// <summary>Поле строки подключения, к которому применяется замена.</summary>
    public ConnectionField SelectedField
    {
        get => _selectedField;
        set
        {
            if (SetProperty(ref _selectedField, value))
                MarkInputChanged();
        }
    }

    /// <summary>Область применения (все базы / выделенные / текущая группа).</summary>
    public ConnectionReplaceScope SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (SetProperty(ref _selectedScope, value))
                MarkInputChanged();
        }
    }

    /// <summary>Режим сопоставления (точное / префикс / подстрока / regex).</summary>
    public ConnectionMatchMode SelectedMatchMode
    {
        get => _selectedMatchMode;
        set
        {
            if (SetProperty(ref _selectedMatchMode, value))
                MarkInputChanged();
        }
    }

    /// <summary>
    /// true — сопоставлять без учёта регистра (по умолчанию включено: «Учитывать
    /// регистр» в окне выключен, см. план функции 6).
    /// </summary>
    public bool IgnoreCase
    {
        get => _ignoreCase;
        set
        {
            if (SetProperty(ref _ignoreCase, value))
                MarkInputChanged();
        }
    }

    /// <summary>
    /// Галочка «Сменить платформу» (issue #54): при включении в операцию массового
    /// действия добавляется смена <see cref="Infobase.PlatformVersion"/> кандидатов на
    /// <see cref="SelectedPlatform"/>. При включении с пустым выбором автоматически
    /// подставляется первая из <see cref="InstalledPlatformVersions"/> (если есть).
    /// </summary>
    public bool ChangePlatform
    {
        get => _changePlatform;
        set
        {
            if (!SetProperty(ref _changePlatform, value))
                return;

            if (value && string.IsNullOrWhiteSpace(_selectedPlatform) &&
                InstalledPlatformVersions.Count > 0)
            {
                SelectedPlatform = InstalledPlatformVersions[0];
            }
            else
            {
                MarkInputChanged();
            }

            OnPropertyChanged(nameof(CanSelectPlatform));
        }
    }

    /// <summary>
    /// Выбранная версия платформы для смены (issue #54). Пустая — смена платформы в
    /// предпросмотре/применении не участвует.
    /// </summary>
    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            if (SetProperty(ref _selectedPlatform, value ?? string.Empty))
                MarkInputChanged();
        }
    }

    /// <summary>
    /// Установленные версии платформы 1С для ComboBox «Сменить платформу» (issue #54):
    /// единый источник передаёт <see cref="MainViewModel.InstalledPlatformVersions"/>.
    /// </summary>
    public IReadOnlyList<string> InstalledPlatformVersions { get; }

    /// <summary>true — поле выбора версии платформы активно (галочка включена и есть версии).</summary>
    public bool CanSelectPlatform => ChangePlatform && InstalledPlatformVersions.Count > 0;

    // ===================== Состояние предпросмотра =====================

    /// <summary>
    /// Входные параметры изменились после последнего «Найти» — предпросмотр устарел,
    /// применение заблокировано до нового поиска.
    /// </summary>
    public bool IsPreviewDirty
    {
        get => _isPreviewDirty;
        private set => SetProperty(ref _isPreviewDirty, value);
    }

    /// <summary>Строки предпросмотра «база | поле | было → станет» (только с изменением).</summary>
    public ObservableCollection<ConnectionReplacePreviewRow> PreviewRows { get; } = new();

    /// <summary>
    /// Варианты поля строки подключения для ComboBox «Поле» (0.3.9.190):
    /// значение enum + локализованный текст (<c>ConnectionReplace.Fields.*</c>).
    /// </summary>
    public IReadOnlyList<DisplayItem<ConnectionField>> Fields { get; }

    /// <summary>
    /// Варианты области применения для ComboBox «Область» (0.3.9.190):
    /// значение enum + локализованный текст (<c>ConnectionReplace.Scopes.*</c>;
    /// для <see cref="ConnectionReplaceScope.BatchSelected"/> подставляется число кандидатов).
    /// </summary>
    public IReadOnlyList<DisplayItem<ConnectionReplaceScope>> Scopes { get; }

    /// <summary>
    /// Варианты режима сопоставления для ComboBox «Режим» (0.3.9.190):
    /// значение enum + локализованный текст (<c>ConnectionReplace.Modes.*</c>).
    /// </summary>
    public IReadOnlyList<DisplayItem<ConnectionMatchMode>> Modes { get; }

    /// <summary>Число уникальных баз, которые будут изменены.</summary>
    public int AffectedCount
    {
        get => _affectedCount;
        private set => SetProperty(ref _affectedCount, value);
    }

    /// <summary>Число кандидатов с подключением, у которых совпадение не найдено.</summary>
    public int NoMatchCount
    {
        get => _noMatchCount;
        private set => SetProperty(ref _noMatchCount, value);
    }

    /// <summary>Число кандидатов без строки подключения (не участвуют).</summary>
    public int EmptyCount
    {
        get => _emptyCount;
        private set => SetProperty(ref _emptyCount, value);
    }

    /// <summary>Строка сводки предпросмотра (ключ <c>ConnectionReplace.Summary.Format</c>).</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>Сводка результата после применения/отката (ключи ConnectionReplace.Result.* / UndoDoneFormat).</summary>
    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>«Заменить» активна только при свежем предпросмотре с совпадениями.</summary>
    public bool CanApply
    {
        get => _canApply;
        private set => SetProperty(ref _canApply, value);
    }

    /// <summary>«Отменить последнюю замену» активна после применения (до следующего отката/применения).</summary>
    public bool CanUndo
    {
        get => _canUndo;
        private set => SetProperty(ref _canUndo, value);
    }

    /// <summary>Сообщение об ошибке (например, невалидное регулярное выражение).</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    // ===================== Команды =====================

    /// <summary>«Найти»: построить предпросмотр и сводку (без мутации баз).</summary>
    public ICommand RefreshPreviewCommand =>
        _refreshPreviewCommand ??= new RelayCommand(RefreshPreview);

    /// <summary>«Заменить»: применить правило (подтверждение выполняет окно, этап 0.3.9.190).</summary>
    public ICommand ApplyCommand =>
        _applyCommand ??= new RelayCommand(Apply);

    /// <summary>«Отменить последнюю замену»: восстановить прежние настройки затронутых баз.</summary>
    public ICommand UndoLastCommand =>
        _undoLastCommand ??= new RelayCommand(UndoLast);

    // ===================== Действия =====================

    /// <summary>
    /// «Найти»: строит правила (строка подключения — если искомый текст непуст; смена
    /// платформы — если включена галочка и выбрана версия) и планы через
    /// <see cref="ConnectionReplacementPlanner.Plan"/> и
    /// <see cref="ConnectionReplacementPlanner.PlanPlatform"/> (без мутаций). Пустой искомый
    /// текст без активной смены платформы — правила не строятся, предпросмотр пуст
    /// (CanApply=false). Невалидный regex-паттерн — <see cref="ArgumentException"/>
    /// превращается в <c>ConnectionReplace.Error.InvalidRegex</c>, предпросмотр пуст.
    /// </summary>
    public void RefreshPreview()
    {
        ResultText = string.Empty;

        var platformRule = BuildPlatformRule();
        if (string.IsNullOrWhiteSpace(FindText) && platformRule is null)
        {
            // Пустой поиск (в т.ч. из пробелов) и смена платформы не задана: правила не
            // строятся, сообщение об ошибке не требуется — пустой предпросмотр с CanApply=false.
            _lastRule = null;
            _lastPlatformRule = null;
            ApplyUpdate(ClearPreview);
            return;
        }

        try
        {
            ConnectionReplacePlan? plan = null;
            if (!string.IsNullOrWhiteSpace(FindText))
            {
                var rule = new ConnectionStringReplaceRule(FindText, ReplaceText, SelectedField, SelectedMatchMode, IgnoreCase);
                plan = ConnectionReplacementPlanner.Plan(_candidates, rule);
                _lastRule = rule;
            }
            else
            {
                _lastRule = null;
            }

            var platformRows = platformRule is null
                ? Array.Empty<ConnectionReplacePreviewRow>()
                : ConnectionReplacementPlanner.PlanPlatform(_candidates, platformRule);
            _lastPlatformRule = platformRule;
            ApplyUpdate(() => ApplyPlan(plan, platformRows));
        }
        catch (ArgumentException ex)
        {
            _lastRule = null;
            _lastPlatformRule = null;
            ApplyUpdate(() =>
            {
                ClearPreview();
                ErrorMessage = string.Format(
                    LocalizationManager.T("ConnectionReplace.Error.InvalidRegex"),
                    ex.Message);
            });
        }
    }

    /// <summary>
    /// «Заменить»: применяет правила через <see cref="ConnectionReplacementPlanner.Apply"/>
    /// (строки подключения) и <see cref="ConnectionReplacementPlanner.ApplyPlatform"/>
    /// (версия платформы), мутирует базы, объединяет записи отката по базе
    /// (<see cref="ConnectionReplacementPlanner.MergeUndoEntries"/>), вызывает колбэк
    /// <c>onApplied</c> (MainViewModel сохраняет undo-историю), выводит сводку результата
    /// (<c>ConnectionReplace.Result.AppliedFormat</c>) и разблокирует «Отменить последнюю
    /// замену». Повторное применение без нового предпросмотра запрещено (CanApply=false).
    /// </summary>
    public void Apply()
    {
        if (!CanApply || IsPreviewDirty)
            return;
        if (_lastRule is null && _lastPlatformRule is null)
            return;

        var entries = new List<ConnectionReplaceUndoEntry>();
        if (_lastRule is not null)
            entries.AddRange(ConnectionReplacementPlanner.Apply(_candidates, _lastRule));
        if (_lastPlatformRule is not null)
            entries.AddRange(ConnectionReplacementPlanner.ApplyPlatform(_candidates, _lastPlatformRule));
        entries = ConnectionReplacementPlanner.MergeUndoEntries(entries).ToList();
        _lastEntries = entries;
        _onApplied?.Invoke(entries);

        ApplyUpdate(() =>
        {
            // Предпросмотр соответствовал состоянию «до» — после применения он устарел:
            // коллекцию сбрасываем, повторное применение без нового «Найти» невозможно.
            PreviewRows.Clear();
            CanApply = false;
            CanUndo = true;
            ResultText = string.Format(
                LocalizationManager.T("ConnectionReplace.Result.AppliedFormat"),
                entries.Count);
        });
    }

    /// <summary>
    /// «Отменить последнюю замену»: восстанавливает прежние настройки через
    /// <see cref="ConnectionReplacementPlanner.Undo"/> и вызывает колбэк <c>onUndone</c>.
    /// Без выполненного применения — no-op.
    /// </summary>
    public void UndoLast()
    {
        if (_lastEntries is null || _lastEntries.Count == 0)
            return;

        var count = _lastEntries.Count;
        var entries = _lastEntries;
        _lastEntries = null;

        ConnectionReplacementPlanner.Undo(entries);
        _onUndone?.Invoke();

        ApplyUpdate(() =>
        {
            PreviewRows.Clear();
            CanUndo = false;
            CanApply = false;
            ResultText = string.Format(
                LocalizationManager.T("ConnectionReplace.UndoDoneFormat"),
                count);
        });
    }

    // ===================== Внутреннее =====================

    /// <summary>Любое изменение входных параметров инвалидирует предпросмотр до нового «Найти».</summary>
    private void MarkInputChanged()
    {
        IsPreviewDirty = true;
        ErrorMessage = string.Empty;
        ApplyUpdate(ClearPreview);
    }

    private void ApplyPlan(ConnectionReplacePlan? plan, IReadOnlyList<ConnectionReplacePreviewRow> platformRows)
    {
        PreviewRows.Clear();
        var affected = new HashSet<Infobase>();
        if (plan is not null)
        {
            foreach (var row in plan.Rows)
            {
                PreviewRows.Add(row);
                affected.Add(row.Infobase);
            }
        }

        var platformAffected = 0;
        foreach (var row in platformRows)
        {
            PreviewRows.Add(row);
            affected.Add(row.Infobase);
            platformAffected++;
        }

        AffectedCount = affected.Count;
        NoMatchCount = plan?.NoMatchCount ?? 0;
        EmptyCount = plan?.EmptyConnectionCount ?? 0;
        var summary = string.Format(
            LocalizationManager.T("ConnectionReplace.Summary.Format"),
            PreviewRows.Count,
            AffectedCount,
            NoMatchCount,
            EmptyCount);
        if (platformAffected > 0)
        {
            // Дополнительный хвост сводки о смене платформы (issue #54).
            summary += string.Format(
                LocalizationManager.T("ConnectionReplace.Summary.PlatformSuffixFormat"),
                platformAffected);
        }

        SummaryText = summary;
        IsPreviewDirty = false;
        CanApply = AffectedCount > 0;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Правило смены платформы из параметров окна: активно, когда галочка включена и
    /// выбрана версия. Пустое значение версии — правило не строится.
    /// </summary>
    private PlatformReplaceRule? BuildPlatformRule()
    {
        if (!ChangePlatform || string.IsNullOrWhiteSpace(SelectedPlatform))
            return null;
        return new PlatformReplaceRule(SelectedPlatform.Trim());
    }

    private void ClearPreview()
    {
        PreviewRows.Clear();
        AffectedCount = 0;
        NoMatchCount = 0;
        EmptyCount = 0;
        SummaryText = string.Empty;
        CanApply = false;
    }

    private void ApplyUpdate(Action action)
    {
        if (_dispatchToUi is null)
            action();
        else
            _dispatchToUi(action);
    }

    // ===================== Локализованные тексты (0.3.9.190) =====================

    /// <summary>Локализованное имя поля строки подключения (<c>ConnectionReplace.Fields.*</c>).</summary>
    public static string FieldDisplayText(ConnectionField field) => field switch
    {
        ConnectionField.Server => LocalizationManager.T("ConnectionReplace.Fields.Server"),
        ConnectionField.Port => LocalizationManager.T("ConnectionReplace.Fields.Port"),
        ConnectionField.Ref => LocalizationManager.T("ConnectionReplace.Fields.Ref"),
        ConnectionField.FilePath => LocalizationManager.T("ConnectionReplace.Fields.FilePath"),
        ConnectionField.WebUrl => LocalizationManager.T("ConnectionReplace.Fields.WebUrl"),
        ConnectionField.Any => LocalizationManager.T("ConnectionReplace.Fields.Any"),
        _ => field.ToString()
    };

    /// <summary>Локализованное имя области применения (<c>ConnectionReplace.Scopes.*</c>).</summary>
    public static string ScopeDisplayText(ConnectionReplaceScope scope, int batchCount = 0) => scope switch
    {
        ConnectionReplaceScope.AllBases => LocalizationManager.T("ConnectionReplace.Scopes.All"),
        ConnectionReplaceScope.BatchSelected => string.Format(
            LocalizationManager.T("ConnectionReplace.Scopes.Selected"), batchCount),
        ConnectionReplaceScope.CurrentGroup => LocalizationManager.T("ConnectionReplace.Scopes.Group"),
        _ => scope.ToString()
    };

    /// <summary>Локализованное имя режима сопоставления (<c>ConnectionReplace.Modes.*</c>).</summary>
    public static string ModeDisplayText(ConnectionMatchMode mode) => mode switch
    {
        ConnectionMatchMode.Exact => LocalizationManager.T("ConnectionReplace.Modes.Exact"),
        ConnectionMatchMode.Prefix => LocalizationManager.T("ConnectionReplace.Modes.Prefix"),
        ConnectionMatchMode.Substring => LocalizationManager.T("ConnectionReplace.Modes.Substring"),
        ConnectionMatchMode.Regex => LocalizationManager.T("ConnectionReplace.Modes.Regex"),
        _ => mode.ToString()
    };
}

/// <summary>
/// Элемент выпадающего списка окна «Заменить в строках подключения…» (0.3.9.190):
/// значение enum, которое пишется в VM-свойство, и локализованный отображаемый текст.
/// <para><see cref="ToString"/> переопределён (issue #357): закрытая часть комбобокса
/// на обеих платформах может рендериться через <c>ToString()</c> выбранного объекта
/// (Avalonia <c>ComboBox.SelectionBoxItem</c> без шаблона), поэтому она обязана
/// возвращать локализованный текст, а не авто-представление записи.</para>
/// </summary>
public sealed record DisplayItem<T>(T Value, string DisplayText)
{
    public override string ToString() => DisplayText;
}