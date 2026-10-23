using System;
using System.Linq;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>
/// Текст уведомления «Приложение обновлено до версии X» (issue #44): к заголовку
/// добавляется «Что нового:» с описанием изменений выпуска и ссылка на страницу
/// релиза — как список изменений в главном окне. Общий для Windows и Linux.
/// </summary>
public static class UpdateNotificationText
{
    /// <summary>Максимальная длина описания изменений в уведомлении (символов).</summary>
    public const int MaxNotesLength = 2000;

    /// <summary>
    /// Собирает текст уведомления об установленном обновлении. Пустое описание не
    /// ломает уведомление — остаётся заголовок (и ссылка, если она есть).
    /// </summary>
    public static string BuildInstalledMessage(string version, string? releaseNotes, string? releaseUrl)
    {
        var header = string.Format(LocalizationManager.T("Notify.UpdateInstalled"), version);
        var notes = PrepareNotes(releaseNotes);
        var message = string.IsNullOrWhiteSpace(notes)
            ? header
            : header + Environment.NewLine + Environment.NewLine +
              LocalizationManager.T("Notify.ReleaseNotes") + Environment.NewLine + notes;

        if (!string.IsNullOrWhiteSpace(releaseUrl))
            message += Environment.NewLine + Environment.NewLine + releaseUrl.Trim();

        return message;
    }

    /// <summary>
    /// Готовит описание выпуска для уведомления: убирает заголовок первого уровня
    /// («# Workspace 1C X» — версия уже в заголовке уведомления), служебную строку
    /// о платформах релиза и хвостовую ссылку на CHANGELOG, схлопывает пустые строки
    /// и обрезает до <see cref="MaxNotesLength"/> символов.
    /// </summary>
    public static string PrepareNotes(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => !l.StartsWith("# ", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("Релиз доступен для", StringComparison.Ordinal))
            .Where(l => !l.StartsWith("Подробности — в [CHANGELOG", StringComparison.Ordinal))
            .ToList();

        // Схлопываем подряд идущие пустые строки и убираем краевые.
        var compact = new System.Collections.Generic.List<string>();
        foreach (var line in lines)
        {
            if (line.Length == 0 && (compact.Count == 0 || compact[^1].Length == 0))
                continue;
            compact.Add(line);
        }
        var text = string.Join(Environment.NewLine, compact).Trim();

        return text.Length <= MaxNotesLength
            ? text
            : text[..MaxNotesLength].TrimEnd() + "…";
    }
}
