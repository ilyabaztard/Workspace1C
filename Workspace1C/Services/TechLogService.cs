using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>Готовые шаблоны настройки технологического журнала.</summary>
public enum TechLogTemplate
{
    /// <summary>Исключения (EXCP) — ошибки платформы.</summary>
    Exceptions,

    /// <summary>Управляемые блокировки: ожидания, таймауты и взаимоблокировки.</summary>
    Locks,

    /// <summary>Долгие запросы к СУБД (DBMSSQL, DBPOSTGRS, DBORACLE, DB2, DBV8DBENG).</summary>
    SlowQueries,

    /// <summary>Долгие серверные вызовы (CALL, SCALL).</summary>
    SlowCalls
}

/// <summary>
/// Настройка технологического журнала 1С (<c>logcfg.xml</c>) по шаблонам
/// (выпуск 1.0.0.69): построение XML, поиск каталога <c>conf</c> платформы,
/// включение с резервной копией прежнего файла и отключение. Срок хранения
/// логов (<c>history</c>, часы) ограничивает рост каталога — платформа сама
/// удаляет устаревшие файлы.
/// </summary>
public static class TechLogService
{
    public const string FileName = "logcfg.xml";

    /// <summary>Каталоги conf, в которых платформа ищет logcfg.xml (существующие).</summary>
    public static IReadOnlyList<string> DetectConfDirectories()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (!string.IsNullOrWhiteSpace(root))
                    candidates.Add(Path.Combine(root, "1cv8", "conf"));
            }
        }
        else
        {
            candidates.Add("/opt/1cv8/conf");
            candidates.Add("/opt/1cv8/x86_64/conf");
            candidates.Add("/opt/1C/v8.3/x86_64/conf");
            candidates.Add("/opt/1C/v8.3/i386/conf");
        }
        return candidates.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Каталог логов по умолчанию.</summary>
    public static string DefaultLogDirectory() => OperatingSystem.IsWindows()
        ? @"C:\1C_TechLog"
        : "/var/log/1c/techlog";

    /// <summary>XML настройки технологического журнала для шаблона.</summary>
    /// <param name="template">Шаблон.</param>
    /// <param name="logDirectory">Каталог логов (создаётся платформой).</param>
    /// <param name="historyHours">Срок хранения логов, часов (1…720).</param>
    /// <param name="thresholdMs">Порог длительности для «долгих» шаблонов, мс.</param>
    public static string BuildXml(TechLogTemplate template, string logDirectory, int historyHours, int thresholdMs)
    {
        var history = Math.Clamp(historyHours, 1, 720).ToString(CultureInfo.InvariantCulture);
        // Длительность в технологическом журнале 8.3 — в микросекундах.
        var threshold = (Math.Max(1, thresholdMs) * 1000L).ToString(CultureInfo.InvariantCulture);
        var location = SecurityElement.Escape(logDirectory?.Trim() ?? string.Empty) ?? string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!-- Создано Workspace 1C: шаблон " + template + ", " +
                      DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " -->");
        sb.AppendLine("<config xmlns=\"http://v8.1c.ru/v8/tech-log\">");
        sb.AppendLine($"  <log location=\"{location}\" history=\"{history}\">");

        void Event(string name, bool withDuration)
        {
            sb.AppendLine("    <event>");
            sb.AppendLine($"      <eq property=\"name\" value=\"{name}\"/>");
            if (withDuration)
                sb.AppendLine($"      <gt property=\"duration\" value=\"{threshold}\"/>");
            sb.AppendLine("    </event>");
        }

        switch (template)
        {
            case TechLogTemplate.Locks:
                Event("TLOCK", false);
                Event("TTIMEOUT", false);
                Event("TDEADLOCK", false);
                break;
            case TechLogTemplate.SlowQueries:
                foreach (var name in new[] { "DBMSSQL", "DBPOSTGRS", "DBORACLE", "DB2", "DBV8DBENG" })
                    Event(name, true);
                break;
            case TechLogTemplate.SlowCalls:
                Event("CALL", true);
                Event("SCALL", true);
                break;
            default:
                Event("EXCP", false);
                break;
        }

        sb.AppendLine("    <property name=\"all\"/>");
        sb.AppendLine("  </log>");
        sb.AppendLine("</config>");
        return sb.ToString();
    }

    /// <summary>Текущий logcfg.xml или пустая строка.</summary>
    public static string ReadCurrent(string confDirectory)
    {
        try
        {
            var path = Path.Combine(confDirectory, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Записывает logcfg.xml (прежний файл сохраняется рядом с суффиксом даты).
    /// Возвращает null при успехе или текст ошибки (обычно — нет прав администратора).
    /// </summary>
    public static string? Enable(string confDirectory, string xml)
    {
        try
        {
            Directory.CreateDirectory(confDirectory);
            var path = Path.Combine(confDirectory, FileName);
            if (File.Exists(path))
                File.Copy(path, path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return LocalizationManager.T("TechLog.NoRights") + " " + ex.Message;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Отключает журнал: logcfg.xml переименовывается (сохраняется для истории).</summary>
    public static string? Disable(string confDirectory)
    {
        try
        {
            var path = Path.Combine(confDirectory, FileName);
            if (!File.Exists(path))
                return null;
            File.Move(path, path + ".off-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            return LocalizationManager.T("TechLog.NoRights") + " " + ex.Message;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Размер каталога логов в байтах (0 — нет каталога).</summary>
    public static long DirectorySize(string? directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;
            return new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
