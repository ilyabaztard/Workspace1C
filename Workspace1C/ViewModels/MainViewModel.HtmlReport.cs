using System;
using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Формирование HTML-отчёта по базам (0.3.9.131), общая часть для обеих платформ.
/// Данные берутся из существующих источников без пересчёта: коллекция
/// <see cref="Infobase"/> (видимость — как в CSV-экспорте, доступность —
/// результат <c>CheckAvailability</c>/<c>SetCheckedAvailability</c>), строки
/// «Центра обслуживания» (<see cref="MaintenanceCenterRowViewModel"/>: размер ИБ,
/// последняя копия, версия конфигурации, критерии проблем) и кэш проверок
/// обновлений из настроек (<see cref="MaintenanceCenterViewModel.LoadUpdateCache"/>).
/// Платформенные части (диалог сохранения, команда меню) живут в
/// <c>MainViewModel.Tools.cs</c> / <c>MainViewModel.Avalonia.Tools.cs</c>.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Собирает документ HTML-отчёта по видимым базам. Набор баз уже отфильтрован
    /// вызывающей стороной (приватные базы заблокированного профиля скрыты — тот же
    /// набор, что в CSV-экспорте).
    /// </summary>
    private HtmlReportDocument BuildHtmlReportData(IEnumerable<Infobase> bases)
    {
        var profileName = "-";
        try
        {
            profileName = AppServices.GetRequiredService<IProfileService>().CurrentProfile?.Name ?? "-";
        }
        catch
        {
            // Тестовый контекст без сервиса профилей — подпись «-».
        }

        var updateCache = MaintenanceCenterViewModel.LoadUpdateCache();
        var freeSpaceWarningGb = MaintenanceFreeSpaceWarningGb;

        var rows = new List<HtmlReportRow>();
        var noteRows = new List<HtmlReportNoteRow>();
        long totalSizeBytes = 0;
        var availableCount = 0;
        var withBackupsCount = 0;
        var problemCount = 0;

        // Заметки баз (п.8, 1.0.0.90): карточки попадают в отчёт отдельной секцией.
        NoteStore? noteStore = null;
        try { noteStore = NoteStoreAccess.GetOrCreate(); } catch { noteStore = null; }

        foreach (var ib in bases)
        {
            // Данные Центра обслуживания: строка считает размер/копию/версию/доступность
            // и признак проблемы по тем же критериям, что окно «Центр обслуживания».
            using var rowVm = new MaintenanceCenterRowViewModel(
                ib,
                MaintenanceCenterViewModel.ResolveUpdateResult(ib, updateCache),
                freeSpaceWarningGb);

            if (rowVm.IsAvailable)
                availableCount++;
            if (ib.LastBackupUtc.HasValue)
                withBackupsCount++;
            if (rowVm.HasProblem)
                problemCount++;

            totalSizeBytes += ib.ManualSizeBytes ?? ib.FileSizeBytes ?? 0;

            rows.Add(new HtmlReportRow
            {
                Name = ib.Name,
                GroupPath = ib.GroupDisplay,
                Type = ib.ConnectionTypeDisplay,
                ConnectionString = ib.ConnectionStringDisplay,
                IsAvailable = rowVm.IsAvailable,
                AvailabilityText = rowVm.AvailabilityText,
                Configuration = rowVm.ConfigurationText,
                Size = rowVm.SizeText,
                LastBackup = rowVm.LastBackupText,
                Modified = FormatCsvModified(ib),
                Tags = string.Join(", ", ib.Tags),
                Favorite = ib.FavoriteHotkeyNumber >= 1 && ib.FavoriteHotkeyNumber <= 9
                    ? ib.FavoriteHotkeyNumber.ToString()
                    : string.Empty,
                HasProblem = rowVm.HasProblem
            });

            // Заметка базы (п.8, 1.0.0.90): только непустые markdown-карточки.
            var note = noteStore?.Get(InfobaseNote.KeyFor(ib));
            if (note is { HasContent: true } && !string.IsNullOrWhiteSpace(note.Markdown))
            {
                noteRows.Add(new HtmlReportNoteRow
                {
                    Name = ib.Name,
                    GroupPath = ib.GroupDisplay,
                    MarkdownHtml = HtmlReportExporter.MarkdownToHtml(note.Markdown)
                });
            }
        }

        // Секция «Заметки» сортируется по группе/имени — как таблицы отчёта.
        noteRows.Sort((a, b) =>
        {
            var byGroup = string.Compare(a.GroupPath, b.GroupPath, StringComparison.OrdinalIgnoreCase);
            return byGroup != 0 ? byGroup : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new HtmlReportDocument
        {
            AppName = LocalizationManager.T("ExportHtml.AppName"),
            GeneratedAtText = string.Format(
                LocalizationManager.T("ExportHtml.GeneratedAt"), DateTime.Now.ToString("dd.MM.yyyy HH:mm")),
            ProfileName = string.Format(LocalizationManager.T("ExportHtml.Profile"), profileName),
            TotalCount = rows.Count,
            AvailableCount = availableCount,
            UnavailableCount = rows.Count - availableCount,
            WithBackupsCount = withBackupsCount,
            TotalSizeBytes = totalSizeBytes,
            ProblemCount = problemCount,
            Labels = BuildHtmlReportLabels(),
            Rows = rows,
            NoteRows = noteRows
        };
    }

    /// <summary>Локализованные подписи HTML-отчёта (генератор остаётся чистым).</summary>
    private static HtmlReportLabels BuildHtmlReportLabels() => new()
    {
        Header = LocalizationManager.T("ExportHtml.Header"),
        SummaryTotal = LocalizationManager.T("ExportHtml.SummaryTotal"),
        SummaryAvailable = LocalizationManager.T("ExportHtml.SummaryAvailable"),
        SummaryUnavailable = LocalizationManager.T("ExportHtml.SummaryUnavailable"),
        SummaryWithBackups = LocalizationManager.T("ExportHtml.SummaryWithBackups"),
        SummarySize = LocalizationManager.T("ExportHtml.SummarySize"),
        SummaryProblems = LocalizationManager.T("ExportHtml.SummaryProblems"),
        ShowAll = LocalizationManager.T("ExportHtml.ShowAll"),
        OnlyProblems = LocalizationManager.T("ExportHtml.OnlyProblems"),
        GroupNav = LocalizationManager.T("ExportHtml.GroupNav"),
        Legend = LocalizationManager.T("ExportHtml.Legend"),
        ColName = LocalizationManager.T("ExportHtml.ColName"),
        ColGroup = LocalizationManager.T("ExportHtml.ColGroup"),
        ColType = LocalizationManager.T("ExportHtml.ColType"),
        ColConnection = LocalizationManager.T("ExportHtml.ColConnection"),
        ColAvailability = LocalizationManager.T("ExportHtml.ColAvailability"),
        ColConfiguration = LocalizationManager.T("ExportHtml.ColConfiguration"),
        ColSize = LocalizationManager.T("ExportHtml.ColSize"),
        ColLastBackup = LocalizationManager.T("ExportHtml.ColLastBackup"),
        ColModified = LocalizationManager.T("ExportHtml.ColModified"),
        ColTags = LocalizationManager.T("ExportHtml.ColTags"),
        ColFavorite = LocalizationManager.T("ExportHtml.ColFavorite"),
        Available = LocalizationManager.T("Maintenance.Available"),
        Unavailable = LocalizationManager.T("Maintenance.Unavailable"),
        NotesHeaderFormat = LocalizationManager.T("ExportHtml.NotesHeader"),
        NotesEmpty = LocalizationManager.T("ExportHtml.NotesEmpty")
    };
}