namespace Workspace1C.Models;

/// <summary>
/// Тип хранилища двусторонней синхронизации своих настроек (п.9 «Синхронизация
/// настроек между компьютерами», 1.0.0.91).
/// </summary>
public enum SettingsSyncStorageKind
{
    /// <summary>WebDAV-каталог (Яндекс.Диск, Nextcloud, ownCloud и др.) — <see cref="SettingsSyncSettings.WebDavUrl"/>.</summary>
    WebDav,

    /// <summary>Сетевая папка (SMB/NFS/локальный общий каталог) — <see cref="SettingsSyncSettings.FolderPath"/>.</summary>
    NetworkFolder
}

/// <summary>
/// Настройки двусторонней синхронизации своих настроек между компьютерами
/// (п.9, 1.0.0.91): тип хранилища (WebDAV / сетевая папка), доступ, набор
/// синхронизируемых файлов (IncludePatterns), интервал автозапуска и момент
/// последней успешной синхронизации. Хранится в файле <c>settings_sync.json</c>
/// каталога данных профиля (см. <see cref="Workspace1C.Services.FeatureDataStore"/>).
/// Пароль WebDAV хранится ТОЛЬКО в зашифрованном виде (<see cref="PasswordEncrypted"/>,
/// шифрование — <see cref="Workspace1C.Services.NoteCrypto"/>, как п.8 в 1.0.0.90).
/// </summary>
public class SettingsSyncSettings
{
    /// <summary>Имя файла настроек рядом с settings.json (каталог данных профиля).</summary>
    public const string FileName = "settings_sync.json";

    /// <summary>
    /// Набор файлов по умолчанию: личный список/группы/настройки (включая горячие клавиши),
    /// пользовательские действия, компоновки меню, удалённые группы, сценарии запуска скриптов
    /// и персональный ibases.v8i (реестр 1С). Каталоги баз могут отличаться между ПК — это
    /// ограничение описано в документации (README), сами локальные пути/пароли не маскируются
    /// и не удаляются при перезаписи файлов.
    /// </summary>
    public static readonly string[] DefaultIncludePatterns =
    {
        "ibases.v8i",                // персональный список 1С (реестр стартера; путь — стандартный)
        "infobases.json",            // личный список баз приложения (включая теги баз)
        "groups.json",               // группы списка
        "settings.json",             // настройки интерфейса, включая горячие клавиши
        "custom_actions.json",       // пользовательские действия контекстного меню
        "custom_config_types.json",  // пользовательские типовые конфигурации
        "context_menu_layout.json",  // компоновка контекстного меню базы
        "tray_menu_layout.json",     // компоновка меню трея
        "utilities_menu_layout.json",// компоновка меню «Утилиты»
        "deleted_groups.json",       // удалённые пустые группы (issue #327)
        "server_ports.json",         // памятка портов серверов (монитор)
        "scripts/scenarios/*.script.json" // сценарии запуска скриптов (script_scenarios)
    };

    /// <summary>Включена ли синхронизация (кнопка «Синхронизировать сейчас» работает и при выключенной опции).</summary>
    public bool Enabled { get; set; }

    /// <summary>Тип хранилища: WebDAV или сетевая папка.</summary>
    public SettingsSyncStorageKind StorageKind { get; set; } = SettingsSyncStorageKind.WebDav;

    /// <summary>Адрес WebDAV-каталога (например «https://webdav.yandex.ru/Workspace1C»).</summary>
    public string WebDavUrl { get; set; } = string.Empty;

    /// <summary>Сетевая (или локальная общая) папка для синхронизации.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Пользователь WebDAV (для сетевой папки не используется).</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Пароль WebDAV в зашифрованном виде (префиксы <c>v1:</c>/<c>v2:</c> — как в
    /// <see cref="Workspace1C.Services.NoteCrypto"/>). Открытое значение не хранится
    /// и не логируется.
    /// </summary>
    public string PasswordEncrypted { get; set; } = string.Empty;

    /// <summary>
    /// Набор файлов для синхронизации: имена файлов (например «settings.json») либо
    /// шаблоны с «*» для вложенных каталогов («scripts/scenarios/*.script.json»).
    /// Пустой список — используются <see cref="DefaultIncludePatterns"/>.
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new(DefaultIncludePatterns);

    /// <summary>
    /// Интервал автозапуска синхронизации в минутах (0 — вручную; <see cref="Enabled"/> — выключатель).
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Момент последней успешной синхронизации (UTC).</summary>
    public System.DateTime? LastSyncAt { get; set; }

    /// <summary>Краткий результат последней синхронизации (без секретов) для окна настроек.</summary>
    public string LastResult { get; set; } = string.Empty;

    /// <summary>Нормализованный список шаблонов (непустой, без дубликатов, с дефолтом).</summary>
    public List<string> EffectiveIncludePatterns =>
        IncludePatterns is { Count: > 0 }
            ? IncludePatterns.Distinct(StringComparer.Ordinal).ToList()
            : new List<string>(DefaultIncludePatterns);
}