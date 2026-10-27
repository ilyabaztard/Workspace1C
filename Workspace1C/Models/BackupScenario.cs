using System.IO;
using System.Linq;
using System.Text;

namespace Workspace1C.Models;

/// <summary>
/// Сценарий резервирования информационной базы: наименование, шаблон имени файла,
/// до трёх каталогов назначения, формат (DT/CF/ZIP/RAR), префикс базы и учётные данные.
/// Хранится в отдельном JSON-файле (см. Services.BackupScenarioStore).
/// </summary>
public class BackupScenario
{
    /// <summary>Идентификатор сценария (GUID).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Наименование сценария.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Шаблон имени файла с подстановками: {Base} — имя/префикс базы,
    /// {Timestamp} — отметка даты-времени, {Date} — дата, {Time} — время.
    /// По умолчанию — «{Base}_{Timestamp}».
    /// </summary>
    public string FileNameTemplate { get; set; } = "{Base}_{Timestamp}";

    /// <summary>Каталоги назначения (от 1 до 3).</summary>
    public List<string> TargetDirectories { get; set; } = new();

    /// <summary>Формат файла резервной копии.</summary>
    public BackupFormat Format { get; set; } = BackupFormat.Dt;

    /// <summary>Движок выгрузки: «Конфигуратор» (по умолчанию) или ibcmd (1.0.0.84).</summary>
    public BackupEngine Engine { get; set; } = BackupEngine.Designer;

    /// <summary>
    /// Префикс базы: подставляется в подстановку {Base} вместе с именем ИБ
    /// (для единообразия имён выгрузок разных баз).
    /// </summary>
    public string BasePrefix { get; set; } = "";

    /// <summary>Учётные данные подключения к ИБ (опционально).</summary>
    public BackupCredential? Credential { get; set; }

    /// <summary>Добавлять отметку даты-времени ({Timestamp}) в имя файла.</summary>
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>
    /// Сколько последних резервных копий хранить в каждом каталоге назначения
    /// (ротация, 0.3.9.86). 0 — хранить все, ничего не удалять по количеству.
    /// </summary>
    public int KeepCount { get; set; }

    /// <summary>
    /// Удалять резервные копии старше указанного числа дней (ротация, 0.3.9.86).
    /// 0 — не чистить по возрасту.
    /// </summary>
    public int DeleteOlderThanDays { get; set; }

    /// <summary>Расширение файла в зависимости от формата (.dt/.cf/.zip/.rar).</summary>
    public string GetExtension() => Format switch
    {
        BackupFormat.Cf => ".cf",
        BackupFormat.Zip => ".zip",
        BackupFormat.Rar => ".rar",
        _ => ".dt"
    };

    /// <summary>
    /// Формирует имя файла по шаблону с подстановками {Base}/{Timestamp}/{Date}/{Time}.
    /// </summary>
    /// <param name="baseName">Имя базы (обычно префикс + имя ИБ) для подстановки {Base}.</param>
    /// <param name="now">Момент времени для отметки даты-времени.</param>
    public string BuildFileName(string baseName, DateTime now)
    {
        var template = string.IsNullOrWhiteSpace(FileNameTemplate)
            ? "{Base}_{Timestamp}"
            : FileNameTemplate;

        var timestamp = now.ToString("yyyyMMdd_HHmmss");
        var date = now.ToString("yyyyMMdd");
        var time = now.ToString("HHmmss");

        var sb = new StringBuilder(template);
        sb.Replace("{Base}", SanitizeFileName(string.IsNullOrWhiteSpace(baseName) ? "base" : baseName));
        if (IncludeTimestamp)
        {
            sb.Replace("{Timestamp}", timestamp);
            sb.Replace("{Date}", date);
            sb.Replace("{Time}", time);
        }
        else
        {
            sb.Replace("{Timestamp}", "");
            sb.Replace("{Date}", "");
            sb.Replace("{Time}", "");
        }

        return SanitizeFileName(sb.ToString());
    }

    /// <summary>Заменяет недопустимые в имени файла символы на «_».</summary>
    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "backup";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "backup" : result;
    }
}