using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Сменить платформу» (issue #54, переработка по комментарию 7OH):
/// отдельное окно из блока «Для выделенных (N)…» — выбор версии платформы 1С из
/// списка установленных и замена <see cref="Infobase.PlatformVersion"/> у выделенных
/// баз по «ОК». Чистый .NET без платформенных зависимостей — обе платформы (WPF и
/// Avalonia); окно только привязывается. Персистентность и undo-история — через
/// колбэк <paramref name="onApplied"/> (тот же <see cref="MainViewModel.ApplyConnectionReplace"/>,
/// что у замены строк подключения: записи отката <see cref="ConnectionReplaceUndoEntry"/>
/// с <c>BeforePlatform</c>/<c>AfterPlatform</c>).
/// </summary>
public sealed class PlatformChangeViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Infobase> _candidates;
    private readonly Action<IReadOnlyList<ConnectionReplaceUndoEntry>>? _onApplied;

    private string _selectedPlatform = string.Empty;
    private string _summaryText = string.Empty;
    private string _resultText = string.Empty;

    private ICommand? _applyCommand;

    /// <param name="candidates">Базы, которым меняется платформа (выделенные в главном списке).</param>
    /// <param name="installedPlatformVersions">
    /// Установленные версии платформы 1С для ComboBox (единый источник —
    /// <see cref="MainViewModel.InstalledPlatformVersions"/>); null/пустой — замена недоступна.
    /// </param>
    /// <param name="onApplied">
    /// Колбэк после применения: <see cref="MainViewModel.ApplyConnectionReplace"/> —
    /// сохранение undo-истории, персистентность, пересборка дерева, выгрузка ibases.v8i.
    /// </param>
    /// <param name="installedPlatformInfos">
    /// Установленные версии с признаком тонкого клиента (единый источник —
    /// <see cref="PlatformVersionService.FindInstalledVersionInfos"/>): по ним в списке
    /// помечаются тонкие клиенты суффиксом и оранжевой подсветкой, как в настройках
    /// (issue #54). null — без пометки (все версии обычные).
    /// </param>
    public PlatformChangeViewModel(
        IReadOnlyList<Infobase> candidates,
        IReadOnlyList<string>? installedPlatformVersions = null,
        Action<IReadOnlyList<ConnectionReplaceUndoEntry>>? onApplied = null,
        IReadOnlyList<PlatformVersionInfo>? installedPlatformInfos = null)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        _onApplied = onApplied;
        InstalledPlatformVersions = (installedPlatformVersions ?? Array.Empty<string>()).ToList();
        InstalledPlatformItems = BuildPlatformItems(InstalledPlatformVersions, installedPlatformInfos);

        // При пустом выборе сразу подставляем первую установленную версию (как галочка
        // «Сменить платформу» в старой реализации окна замены строк, issue #54).
        if (InstalledPlatformVersions.Count > 0)
            SelectedPlatform = InstalledPlatformVersions[0];

        _summaryText = InstalledPlatformVersions.Count == 0
            ? LocalizationManager.T("PlatformChange.EmptyList")
            : string.Format(LocalizationManager.T("PlatformChange.SummaryFormat"), _candidates.Count);
    }

    /// <summary>Установленные версии платформы 1С для выбора.</summary>
    public IReadOnlyList<string> InstalledPlatformVersions { get; }

    /// <summary>
    /// Установленные версии платформы 1С для ComboBox с признаком тонкого клиента
    /// (issue #54): <see cref="PlatformVersionInfo.DisplayName"/> содержит локализованный
    /// суффикс «(тонкий клиент)», а <see cref="PlatformVersionInfo.IsThinClient"/> позволяет
    /// подсветить такие версии оранжевым, как в настройках (issue #33).
    /// </summary>
    public IReadOnlyList<PlatformVersionInfo> InstalledPlatformItems { get; }

    /// <summary>
    /// Строит элементы списка из строк версий, помечая тонкие клиенты по
    /// <paramref name="infos"/> (единый источник — <see cref="PlatformVersionService.FindInstalledVersionInfos"/>).
    /// Порядок и состав повторяют <paramref name="versions"/>; версии, которых нет в
    /// <paramref name="infos"/>, считаются обычными.
    /// </summary>
    private static IReadOnlyList<PlatformVersionInfo> BuildPlatformItems(
        IReadOnlyList<string> versions,
        IReadOnlyList<PlatformVersionInfo>? infos)
    {
        var thinClients = new HashSet<string>(
            (infos ?? Array.Empty<PlatformVersionInfo>())
                .Where(i => i.IsThinClient)
                .Select(i => i.Display),
            StringComparer.OrdinalIgnoreCase);

        return versions
            .Select(s => new PlatformVersionInfo
            {
                Display = s,
                IsThinClient = thinClients.Contains(s)
            })
            .ToList();
    }

    /// <summary>Выбранная версия платформы (пустая — применение недоступно).</summary>
    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set => SetProperty(ref _selectedPlatform, value ?? string.Empty);
    }

    /// <summary>Сводка: «Будет изменено баз: N» (или предупреждение о пустом списке платформ).</summary>
    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>Сводка результата после применения («Платформа заменена: баз N»).</summary>
    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    /// <summary>«Сменить» доступна, когда есть установленные платформы, кандидаты и выбранная версия.</summary>
    public bool CanApply =>
        InstalledPlatformVersions.Count > 0
        && _candidates.Count > 0
        && !string.IsNullOrWhiteSpace(SelectedPlatform);

    /// <summary>«Сменить»: заменить платформу кандидатов на выбранную.</summary>
    public ICommand ApplyCommand => _applyCommand ??= new RelayCommand(Apply);

    /// <summary>
    /// Применяет смену платформы через <see cref="ConnectionReplacementPlanner.ApplyPlatform"/>,
    /// вызывает колбэк <c>onApplied</c> (undo-история и персистентность) и показывает сводку.
    /// Повторный вызов без изменения версии не меняет записи (ApplyPlatform пропускает
    /// базы, у которых версия уже совпадает).
    /// </summary>
    public void Apply()
    {
        if (!CanApply)
            return;

        var entries = ConnectionReplacementPlanner.ApplyPlatform(
            _candidates, new PlatformReplaceRule(SelectedPlatform.Trim()));
        _onApplied?.Invoke(entries);

        ResultText = string.Format(
            LocalizationManager.T("PlatformChange.AppliedFormat"),
            entries.Count);
    }
}