using System.Text.Json.Serialization;

namespace Workspace1C.Models;

/// <summary>
/// Заметка информационной базы (п.8 «Заметки к базе», выпуск 1.0.0.90): markdown-карточка
/// («для кого база», контакты, особенности обновления) + зашифрованные значения секретов
/// внешних сервисов (например, имя пользователя/пароль/URL). Хранится в отдельном файле
/// <c>infobase_notes.json</c> в каталоге данных профиля, привязка — по ключу базы
/// (<see cref="Infobase.Id"/> — GUID 1С; fallback — имя базы, см. <see cref="KeyFor"/>).
/// Значения <see cref="Secrets"/> лежат в хранилище УЖЕ зашифрованными (DPAPI на Windows /
/// AES-GCM на Linux через <see cref="Services.NoteCrypto"/>) — в открытом виде ни на диск,
/// ни в логи не попадают.
/// </summary>
public sealed class InfobaseNote
{
    /// <summary>
    /// Ключ базы, к которой относится заметка: <see cref="Infobase.Id"/> (GUID из ibases.v8i)
    /// либо имя базы, если Id пуст. Стабилен при импорте из общих списков (ID переносится,
    /// issue #278), поэтому заметка остаётся доступной для той же базы после синхронизации.
    /// </summary>
    public string InfobaseId { get; set; } = string.Empty;

    /// <summary>Markdown-текст карточки базы (обычный текст с разметкой, рендер — MarkdownRenderer).</summary>
    public string Markdown { get; set; } = string.Empty;

    /// <summary>
    /// Секреты внешних сервисов: имя (например «t.me/админ», «1С-Фреш», «WebDav») →
    /// ЗАШИФРОВАННОЕ значение (префикс <c>v1:</c>/<c>v2:</c> см. <see cref="Services.NoteCrypto"/>).
    /// Расшифровка — только на устройстве, где был создан секрет (DPAPI-ключ пользователя
    /// Windows или ключовой файл профиля Linux). При недоступности ключа (переезд между ПК)
    /// UI показывает «зашифровано, ключ недоступен» вместо значения.
    /// </summary>
    public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Момент последнего сохранения заметки (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Вычисляет ключ базы для привязки заметки: приоритет у стабильного ID 1С
    /// (<see cref="Infobase.Id"/>), fallback — имя базы (базы без ID, добавленные вручную).
    /// Пустая база/имя → пустой ключ (заметка не создаётся).
    /// </summary>
    public static string KeyFor(Infobase? infobase)
    {
        if (infobase is null)
            return string.Empty;
        var id = infobase.Id?.Trim();
        if (!string.IsNullOrEmpty(id))
            return id;
        var name = infobase.Name?.Trim();
        return name ?? string.Empty;
    }

    /// <summary>
    /// True — заметка «полезная» (есть текст карточки или хотя бы один секрет):
    /// пустые записи не создаются и не сохраняются.
    /// </summary>
    [JsonIgnore]
    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Markdown) || (Secrets is not null && Secrets.Count > 0);

    /// <summary>Имена секретов для поиска/отображения (пустой список — секретов нет).</summary>
    [JsonIgnore]
    public IEnumerable<string> SecretNames => Secrets?.Keys ?? Enumerable.Empty<string>();
}
