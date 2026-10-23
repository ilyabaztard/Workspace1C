using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат импорта настроек из программы StartManager (issue #163).
/// </summary>
public class StartManagerImportResult
{
    /// <summary>Количество добавленных новых баз.</summary>
    public int Added { get; set; }

    /// <summary>Количество обновлённых существующих баз.</summary>
    public int Updated { get; set; }

    /// <summary>
    /// Количество пропущенных секций StartManager: у них нет пары в списке баз 1С,
    /// то есть база из списка удалена, а настройки StartManager остались.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>Количество созданных групп.</summary>
    public int GroupsCreated { get; set; }

    /// <summary>
    /// Каталоги установки платформы 1С, определённые из settings.cnf.
    /// Подходят для добавления в дополнительные пути поиска платформы приложения.
    /// </summary>
    public List<string> PlatformSearchPaths { get; } = new();

    /// <summary>Путь к каталогу, из которого выполнен импорт (для сообщений пользователю).</summary>
    public string SourceDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Путь к списку баз 1С (ibases.v8i), из которого взяты строки подключения,
    /// или null, если файл не найден.
    /// </summary>
    public string? IbasesPath { get; set; }

    /// <summary>
    /// Признак того, что список баз 1С (ibases.v8i) не найден. Без него импорт
    /// невозможен: строки подключения StartManager не хранит.
    /// </summary>
    public bool NoIbasesFound { get; set; }

    /// <summary>
    /// Признак того, что пароли не переносились: в StartManager включён неизвестный
    /// метод шифрования (NewMethodEncryption).
    /// </summary>
    public bool PasswordsSkipped { get; set; }

    /// <summary>Признак того, что файл v8config.smc отсутствует (импорт ничего не сделал).</summary>
    public bool NoConfigFound { get; set; }
}

/// <summary>
/// Сервис импорта настроек из программы «StartManager» — альтернативного стартера 1С.
/// Читает два файла из каталога настроек StartManager (%APPDATA%\StartManager14\SMSettings):
/// <list type="bullet">
///   <item><c>settings.cnf</c> — общие настройки, включая путь к платформе 1С (V8AppPath);</item>
///   <item><c>v8config.smc</c> — надстройки StartManager к базам списка 1С:
///       авторизации, хранилище, версия конфигурации, флаги запуска.</item>
/// </list>
/// Пароли в StartManager зашифрованы методом Виженера по ASCII-символам с ключом «SLAVKA240601» —
/// здесь реализована их расшифровка. Кодировка файлов определяется по BOM:
/// StartManager 1.4 пишет UTF-8, более старые сборки — Windows-1251.
/// </summary>
/// <remarks>
/// Класс не зависит от UI и компилируется в обеих сборках (WPF и Avalonia).
/// </remarks>
public static class StartManagerImporter
{
    /// <summary>Ключ шифрования паролей StartManager (метод Виженера по ASCII).</summary>
    private const string VigenereKey = "SLAVKA240601";

    /// <summary>Код символа «0»: постоянное слагаемое шифра паролей StartManager.</summary>
    private const int ZeroDigit = '0';

    /// <summary>Имя файла списка баз StartManager.</summary>
    private const string ConfigFileName = "v8config.smc";

    /// <summary>Имя файла общих настроек StartManager.</summary>
    private const string SettingsFileName = "settings.cnf";

    // Ключи секции базы в v8config.smc. Набор сверен с файлами StartManager 1.4:
    // у настроенной базы 28 ключей, ключей строки подключения среди них нет.
    // Подключение хранится в ibases.v8i платформы (issue #163). Секция базы, которую
    // только запускали, содержит один-два ключа и настроек не несёт.
    private const string KConfigVersion = "ConfigVersion";
    private const string KDescription = "Description";
    private const string KNote = "Note";
    private const string KUserStorage = "UserStorage";
    private const string KStorageDir = "StorageDir";
    private const string KStorageUser = "StorageUser";
    private const string KStoragePassword = "StoragePassword";
    private const string KUserLoginEnt = "UserLoginEnt";
    private const string KEntUser = "EntUser";
    private const string KEntPassword = "EntPassword";
    private const string KUserLoginCnf = "UserLoginCnf";
    private const string KCfgUser = "CfgUser";
    private const string KCfgPassword = "CfgPassword";

    /// <summary>Элемент settings.cnf с признаком нового метода шифрования паролей.</summary>
    private const string KNewEncryption = "NewMethodEncryption";

    /// <summary>Ключ settings.cnf с путём к исполняемому файлу платформы 1С.</summary>
    private const string KV8AppPath = "V8AppPath";

    private static readonly Encoding Ansi = CreateAnsiEncoding();

    /// <summary>
    /// Возвращает кодировку ANSI (Windows-1251) для чтения файлов StartManager.
    /// Кодовые страницы в .NET доступны только после регистрации провайдера, иначе
    /// <see cref="Encoding.GetEncoding(int)"/> бросает исключение и кодировкой становится
    /// UTF-8: тогда пароль с кириллицей не расшифровать, потому что шифр StartManager
    /// работает по однобайтовым кодам символов.
    /// </summary>
    private static Encoding CreateAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }
        catch
        {
            return Encoding.Default;
        }
    }

    /// <summary>
    /// Возвращает стандартный каталог настроек StartManager:
    /// <c>%APPDATA%\StartManager14\SMSettings</c>. На Linux дополнительно проверяется
    /// каталог с тем же именем в домашнем каталоге (обычно копия, перенесённая с Windows).
    /// </summary>
    public static string? FindDefaultSettingsDir()
    {
        var candidates = new List<string>();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
            candidates.Add(Path.Combine(appData, "StartManager14", "SMSettings"));

        // На Linux ApplicationData возвращает ~/.config; каталог, перенесённый с Windows,
        // обычно лежит прямо в домашнем каталоге, поэтому пробуем и его.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            candidates.Add(Path.Combine(home, "StartManager14", "SMSettings"));

        // Существующий каталог важнее порядка: иначе на не-Windows возвращался бы
        // несуществующий путь из ApplicationData, а домашний каталог не проверялся вовсе.
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// Импортирует базы из StartManager в коллекции приложения: добавляет новые базы,
    /// обновляет существующие (по совпадению имени) и создаёт недостающие группы.
    /// Также читает settings.cnf и возвращает каталоги платформы 1С.
    /// </summary>
    /// <param name="settingsDir">Каталог настроек StartManager (SMSettings).</param>
    /// <param name="infobases">Коллекция баз приложения, в которую выполняется импорт.</param>
    /// <param name="groups">Коллекция групп приложения, в которую добавляются недостающие группы.</param>
    /// <param name="ibasesFilePath">
    /// Путь к списку баз 1С (ibases.v8i). Если не задан, используется стандартный путь.
    /// Строки подключения StartManager не хранит, поэтому без этого файла импорт невозможен.
    /// </param>
    /// <returns>Результат импорта.</returns>
    public static StartManagerImportResult Import(
        string settingsDir,
        IList<Infobase> infobases,
        IList<Group> groups,
        string? ibasesFilePath = null)
    {
        var result = new StartManagerImportResult { SourceDirectory = settingsDir };

        var configPath = Path.Combine(settingsDir, ConfigFileName);
        if (!File.Exists(configPath))
        {
            result.NoConfigFound = true;
            return result;
        }

        var sections = ParseSmcFile(configPath);

        // Пути платформы 1С из settings.cnf собираем до проверок: они не зависят
        // от списка баз и полезны пользователю, даже если импортировать нечего.
        var settingsPath = Path.Combine(settingsDir, SettingsFileName);
        CollectPlatformPaths(settingsPath, result.PlatformSearchPaths);

        // StartManager умеет шифровать пароли двумя способами, и второй нам неизвестен.
        // Расшифровать его тем же ключом нельзя: в поля паролей уехал бы мусор, а
        // пользователь увидел бы не «импорт не смог», а отказ 1С в доступе.
        var passwordsUsable = !UsesUnknownPasswordEncryption(settingsPath);
        if (!passwordsUsable)
        {
            result.PasswordsSkipped = true;
            LogInfo("Импорт из StartManager: включён неизвестный метод шифрования паролей "
                    + "(NewMethodEncryption), пароли не переносятся.");
        }

        // Строки подключения в файлах StartManager нет: имя секции v8config.smc — это
        // идентификатор базы из списка 1С, а подключение хранит сама платформа
        // в ibases.v8i. Поэтому список баз читаем оттуда, а из StartManager берём
        // только его надстройки к базе (issue #163).
        // Путь к списку баз: заданный пользователем в настройках, иначе стандартный —
        // так же, как его определяют остальные операции приложения с ibases.v8i.
        var ibasesPath = string.IsNullOrWhiteSpace(ibasesFilePath)
            ? IbasesV8iImporter.FindDefaultPath()
            : ibasesFilePath.Trim();
        result.IbasesPath = ibasesPath;
        if (string.IsNullOrWhiteSpace(ibasesPath) || !File.Exists(ibasesPath))
        {
            result.NoIbasesFound = true;
            LogInfo("Импорт из StartManager: список баз 1С (ibases.v8i) не найден, импортировать нечего.");
            return result;
        }

        var basesById = new Dictionary<string, Infobase>(StringComparer.OrdinalIgnoreCase);
        foreach (var fromList in IbasesV8iImporter.ReadInfobases(ibasesPath))
        {
            var listId = (fromList.Id ?? string.Empty).Trim();
            if (listId.Length > 0)
                basesById[listId] = fromList;
        }

        var imported = new List<Infobase>();
        foreach (var section in sections)
        {
            var sectionId = SectionName(section).Trim();
            if (sectionId.Length == 0 || !basesById.TryGetValue(sectionId, out var infobase))
            {
                // Секция без пары в списке баз: база удалена из 1С, а StartManager
                // помнит её настройки. Заводить такую базу заново не нужно.
                result.Skipped++;
                continue;
            }

            ApplyStartManagerSettings(infobase, section, passwordsUsable);
            imported.Add(infobase);
        }

        // Недостающие группы создаём до добавления баз и тем же разбором, что и обычный
        // импорт списка баз: там учтены нормализация путей, осиротевшие родители,
        // идентификаторы групп из файла и уборка дубликатов (issue #165).
        result.GroupsCreated = IbasesV8iImporter.EnsureGroupsFromFile(ibasesPath, groups);

        foreach (var infobase in imported)
        {
            var existing = FindExisting(infobases, infobase);

            if (existing is null)
            {
                infobases.Add(infobase);
                result.Added++;

                // Журнал добавления по строке на базу: по нему видно, что именно приехало
                // из StartManager, без открытия карточки каждой базы (issue #163).
                LogInfo(
                    $"Добавлена база «{infobase.Name}» из StartManager: " +
                    $"подключение=[{infobase.Connection.ToConnectionString()}], " +
                    $"группа=[{(string.IsNullOrWhiteSpace(infobase.Group) ? "—" : infobase.Group)}], " +
                    $"хранилище={(infobase.Repository is { HasServer: true } ? "задано" : "—")}, " +
                    $"Предприятие={(infobase.EnterpriseAuth is { IsDefault: false } ? "задано" : "—")}, " +
                    $"Конфигуратор={(infobase.ConfiguratorAuth is { IsDefault: false } ? "задано" : "—")}");
            }
            else
            {
                // Режим слияния при повторном импорте (issue #163): дополняем/перезаписываем
                // настройки подключения, группу и авторизации существующей базы, а не только
                // добавляем новые. Логин/пароль существующей базы не затираем, если в
                // StartManager они не заданы. Так «удалённые вручную» авторизации (хранилище /
                // Предприятие / Конфигуратор) восстанавливаются из StartManager.
                Merge(existing, infobase);
                result.Updated++;

                // Лог слияния (issue #163): по журналу видно, какие авторизации были
                // восстановлены, а какие остались пустыми — ускоряет подтверждение на
                // машине пользователя без угадывания.
                LogInfo(
                    $"Слияние базы «{infobase.Name}» из StartManager: " +
                    $"группа=[{(string.IsNullOrWhiteSpace(existing.Group) ? "—" : existing.Group)}], " +
                    $"хранилище={(existing.Repository is { HasServer: true } ? "задано" : "—")}, " +
                    $"Предприятие={(existing.EnterpriseAuth is { IsDefault: false } ? "задано" : "—")}, " +
                    $"Конфигуратор={(existing.ConfiguratorAuth is { IsDefault: false } ? "задано" : "—")}");
            }
        }

        LogInfo(
            $"Импорт из StartManager: список баз {ibasesPath}, "
            + $"пропущено секций без базы в нём {result.Skipped}, создано групп {result.GroupsCreated}.");

        return result;
    }

    /// <summary>
    /// Переносит настройки импортированной базы в существующую. Имя, группа и строка
    /// подключения приходят из списка баз 1С и переносятся как есть. Надстройки
    /// StartManager (авторизации, хранилище, версия конфигурации, описание) переносятся
    /// только когда они в StartManager заданы: он хранит их лишь для баз, которые
    /// пользователь там настраивал, и пустое значение в нём не означает «очистить»
    /// (issue #163).
    /// </summary>
    private static void Merge(Infobase target, Infobase imported)
    {
        if (!string.IsNullOrWhiteSpace(imported.Id))
            target.Id = imported.Id;

        var importedName = (imported.Name ?? string.Empty).Trim();
        if (importedName.Length > 0)
            target.Name = importedName;

        if (!string.IsNullOrWhiteSpace(imported.Group))
            target.Group = imported.Group;

        // Строка подключения приходит из списка баз 1С. Учётные данные самой строки
        // подключения там не хранятся, поэтому заданные в приложении сохраняем,
        // включая режим аутентификации: иначе база начнёт спрашивать пароль при
        // каждом запуске (та же грабля учтена в IbasesV8iImporter).
        var prevUser = target.Connection.User;
        var prevPassword = target.Connection.Password;
        var prevAuthMode = target.Connection.AuthenticationMode;
        target.Connection = imported.Connection;
        if (string.IsNullOrWhiteSpace(target.Connection.User) && !string.IsNullOrWhiteSpace(prevUser))
            target.Connection.User = prevUser;
        if (string.IsNullOrWhiteSpace(target.Connection.Password) && !string.IsNullOrWhiteSpace(prevPassword))
            target.Connection.Password = prevPassword;
        if (target.Connection.AuthenticationMode == AuthenticationMode.Prompt
            && prevAuthMode != AuthenticationMode.Prompt
            && (!string.IsNullOrWhiteSpace(target.Connection.User)
                || !string.IsNullOrWhiteSpace(target.Connection.Password)))
        {
            target.Connection.AuthenticationMode = prevAuthMode;
        }

        // Сведения о базе из списка 1С (те же поля переносит IbasesV8iImporter).
        if (!string.IsNullOrWhiteSpace(imported.PlatformVersion))
            target.PlatformVersion = imported.PlatformVersion;
        if (imported.Architecture is "32" or "64")
            target.Architecture = imported.Architecture;
        // Режим запуска переносим только если он в файле задан явно: «Автоматический»
        // приходит и как штатное значение ключа App (режима пользователь не задавал),
        // и не должен затирать «Толстый клиент», поставленный в приложении (issue #59;
        // в IbasesV8iImporter для этого используется IbaseEntry.HasExplicitLaunchMode).
        if (!string.IsNullOrWhiteSpace(imported.LaunchMode)
            && !string.Equals(imported.LaunchMode, "Автоматический", StringComparison.OrdinalIgnoreCase))
        {
            target.LaunchMode = imported.LaunchMode;
        }
        if (!string.IsNullOrWhiteSpace(imported.LaunchParameters))
            target.LaunchParameters = imported.LaunchParameters;

        // Надстройки StartManager: переносим только заданные.
        if (!string.IsNullOrWhiteSpace(imported.ConfigurationVersion))
            target.ConfigurationVersion = imported.ConfigurationVersion;
        if (!string.IsNullOrWhiteSpace(imported.Description))
            target.Description = imported.Description;

        MergeRepository(target, imported.Repository);
        target.EnterpriseAuth = MergeAuthSettings(target.EnterpriseAuth, imported.EnterpriseAuth);
        target.ConfiguratorAuth = MergeAuthSettings(target.ConfiguratorAuth, imported.ConfiguratorAuth);
    }

    /// <summary>
    /// Переносит настройки хранилища конфигурации из StartManager. Пустой источник
    /// означает, что хранилище там не настраивалось, и существующие настройки базы
    /// не трогаются: StartManager хранит хранилище лишь для тех баз, где пользователь
    /// его задал (на эталонном профиле это 1 база из 56).
    /// </summary>
    private static void MergeRepository(Infobase target, RepositorySettings? imported)
    {
        var importedEmpty = imported is null
            || (!imported.HasServer
                && string.IsNullOrWhiteSpace(imported.RepositoryName)
                && string.IsNullOrWhiteSpace(imported.User)
                && string.IsNullOrWhiteSpace(imported.Password));
        if (importedEmpty)
            return;

        var dst = target.Repository;
        var dstEmpty = !dst.HasServer
                       && string.IsNullOrWhiteSpace(dst.RepositoryName)
                       && string.IsNullOrWhiteSpace(dst.User)
                       && string.IsNullOrWhiteSpace(dst.Password);
        if (dstEmpty)
        {
            target.Repository = imported!;
            return;
        }

        // Хранилище задано с обеих сторон: заполненные поля источника переносим,
        // пустые оставляем как есть.
        if (!string.IsNullOrWhiteSpace(imported!.Server))
            dst.Server = imported.Server;
        if (!string.IsNullOrWhiteSpace(imported.RepositoryName))
            dst.RepositoryName = imported.RepositoryName;
        if (!string.IsNullOrWhiteSpace(imported.User))
            dst.User = imported.User;
        if (!string.IsNullOrWhiteSpace(imported.Password))
            dst.Password = imported.Password;
    }

    /// <summary>
    /// Переносит авторизацию («1С:Предприятие» или «Конфигуратор») из StartManager.
    /// Пустой источник означает, что там она не задана, и авторизация базы остаётся
    /// прежней. Возвращает итоговую авторизацию для записи в базу.
    /// </summary>
    private static InfobaseAuthSettings? MergeAuthSettings(
        InfobaseAuthSettings? target,
        InfobaseAuthSettings? imported)
    {
        if (imported is null || imported.IsDefault)
            return target;

        if (target is null || target.IsDefault)
            return imported;

        if (!string.IsNullOrWhiteSpace(imported.User))
            target.User = imported.User;
        if (!string.IsNullOrWhiteSpace(imported.Password))
            target.Password = imported.Password;
        target.AuthenticationMode = imported.AuthenticationMode;

        return target;
    }

    /// <summary>Пишет информационное сообщение импорта в файловый лог (issue #163).</summary>
    private static void LogInfo(string message)
    {
        try
        {
            AppServices.GetRequiredService<IAppLogger>().Info(message);
        }
        catch
        {
            // Логирование не должно ломать импорт.
        }
    }

    /// <summary>
    /// Находит существующую базу приложения для слияния с импортируемой записью
    /// (режим слияния при повторном импорте, issue #163). Поиск выполняется
    /// последовательно по нескольким критериям, чтобы не создавать дубликаты и
    /// корректно обновлять авторизации существующих баз:
    /// <list type="number">
    ///   <item>точное совпадение имени (как было ранее);</item>
    ///   <item>совпадение идентификатора StartManager / приложения (ID);</item>
    ///   <item>совпадение нормализованной строки подключения (путь / сервер+база / URL).</item>
    /// </list>
    /// Возвращает null, если подходящая база не найдена (тогда добавляется новая).
    /// </summary>
    private static Infobase? FindExisting(IList<Infobase> infobases, Infobase imported)
    {
        var name = (imported.Name ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var byName = infobases.FirstOrDefault(b =>
                string.Equals((b.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        var id = (imported.Id ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(id))
        {
            var byId = infobases.FirstOrDefault(b =>
                !string.IsNullOrWhiteSpace(b.Id)
                && string.Equals((b.Id ?? string.Empty).Trim(), id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        var conn = imported.Connection?.ToConnectionString();
        if (!string.IsNullOrWhiteSpace(conn))
        {
            var byConn = infobases.FirstOrDefault(b =>
                b.Connection is not null
                && !string.IsNullOrWhiteSpace(b.Connection.ToConnectionString())
                && string.Equals(b.Connection.ToConnectionString(), conn, StringComparison.OrdinalIgnoreCase));
            if (byConn is not null)
                return byConn;
        }

        return null;
    }

    /// <summary>
    /// Накладывает надстройки StartManager на базу, прочитанную из списка 1С:
    /// авторизации «Предприятия» и «Конфигуратора», хранилище конфигурации,
    /// версию конфигурации и заметку. Подключение, имя и группа приходят
    /// из ibases.v8i и здесь не меняются.
    /// </summary>
    private static void ApplyStartManagerSettings(
        Infobase infobase,
        Dictionary<string, string> section,
        bool passwordsUsable)
    {
        var repository = BuildRepository(section, passwordsUsable);
        if (repository is not null)
            infobase.Repository = repository;

        var enterprise = BuildEnterpriseAuth(section, passwordsUsable);
        if (enterprise is not null)
            infobase.EnterpriseAuth = enterprise;

        var configurator = BuildConfiguratorAuth(section, passwordsUsable);
        if (configurator is not null)
            infobase.ConfiguratorAuth = configurator;

        var configVersion = Get(section, KConfigVersion);
        if (!string.IsNullOrWhiteSpace(configVersion))
            infobase.ConfigurationVersion = configVersion.Trim();

        // Описание базы: заметка приоритетнее, но и само описание StartManager
        // несёт данные, которых нет в списке 1С (обычно уточнённая редакция
        // конфигурации). Если оно повторяет имя базы, писать его незачем.
        var note = Get(section, KNote);
        var description = Get(section, KDescription);
        if (!string.IsNullOrWhiteSpace(note))
            infobase.Description = note.Trim();
        else if (!string.IsNullOrWhiteSpace(description)
                 && !string.Equals(description.Trim(), infobase.Name?.Trim(), StringComparison.Ordinal))
            infobase.Description = description.Trim();
    }

    /// <summary>
    /// Строит настройки хранилища конфигурации (UserStorage / StorageDir / ...).
    /// Возвращает null, если авторизация в хранилище не используется или не заполнена.
    /// </summary>
    private static RepositorySettings? BuildRepository(Dictionary<string, string> section, bool passwordsUsable)
    {
        if (!HasTrue(section, KUserStorage))
            return null;

        var dir = Get(section, KStorageDir);
        var user = Get(section, KStorageUser);
        var password = passwordsUsable ? DecryptPassword(Get(section, KStoragePassword)) : string.Empty;

        if (string.IsNullOrWhiteSpace(dir) && string.IsNullOrWhiteSpace(user))
            return null;

        var repo = new RepositorySettings
        {
            Server = dir?.Trim() ?? string.Empty,
            User = user ?? string.Empty,
            Password = password
        };

        // StorageDir вида «tcp://server:1542/ИмяХранилища» или «tcp://server:1542\ИмяХранилища»
        // — выделяем имя хранилища, отделяя последний разделитель пути («/» или «\»).
        var dirValue = dir?.Trim();
        if (!string.IsNullOrEmpty(dirValue))
        {
            // Отделяем префикс схемы «tcp://», «file://» и т.п. до «://»,
            // чтобы его слеши не считались разделителями пути (issue #163).
            var schemePrefix = string.Empty;
            var body = dirValue;
            var schemeIdx = dirValue.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                schemePrefix = dirValue[..(schemeIdx + 3)];
                body = dirValue[(schemeIdx + 3)..];
            }

            // Ищем позицию последнего из двух разделителей пути.
            var slashIdx = body.LastIndexOf('/');
            var backslashIdx = body.LastIndexOf('\\');
            var idx = Math.Max(slashIdx, backslashIdx);

            // Разделяем, только если после разделителя есть непустой сегмент
            // (иначе имя хранилища не заполняем, Server оставляем без изменений).
            if (idx >= 0 && idx < body.Length - 1)
            {
                repo.RepositoryName = body.Substring(idx + 1).Trim();
                repo.Server = (schemePrefix + body.Substring(0, idx)).Trim();
            }
        }

        return repo;
    }

    /// <summary>Строит авторизацию «1С:Предприятие» (UserLoginEnt / EntUser / EntPassword).</summary>
    private static InfobaseAuthSettings? BuildEnterpriseAuth(Dictionary<string, string> section, bool passwordsUsable)
    {
        if (!HasTrue(section, KUserLoginEnt))
            return null;

        var user = Get(section, KEntUser);
        var password = passwordsUsable ? DecryptPassword(Get(section, KEntPassword)) : string.Empty;
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password))
            return null;

        return new InfobaseAuthSettings
        {
            AuthenticationMode = string.IsNullOrWhiteSpace(user)
                ? AuthenticationMode.Prompt
                : AuthenticationMode.Credentials,
            User = user ?? string.Empty,
            Password = password
        };
    }

    /// <summary>Строит авторизацию «Конфигуратора» (UserLoginCnf / CfgUser / CfgPassword).</summary>
    private static InfobaseAuthSettings? BuildConfiguratorAuth(Dictionary<string, string> section, bool passwordsUsable)
    {
        if (!HasTrue(section, KUserLoginCnf))
            return null;

        var user = Get(section, KCfgUser);
        var password = passwordsUsable ? DecryptPassword(Get(section, KCfgPassword)) : string.Empty;
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(password))
            return null;

        return new InfobaseAuthSettings
        {
            AuthenticationMode = string.IsNullOrWhiteSpace(user)
                ? AuthenticationMode.Prompt
                : AuthenticationMode.Credentials,
            User = user ?? string.Empty,
            Password = password
        };
    }

    /// <summary>
    /// Извлекает каталоги платформы 1С из settings.cnf и добавляет их в список
    /// <paramref name="target"/>, исключая повторы и несуществующие каталоги.
    /// </summary>
    private static void CollectPlatformPaths(string settingsPath, List<string> target)
    {
        if (!File.Exists(settingsPath))
            return;

        var candidates = new List<string>();
        foreach (var exe in ReadPlatformExecutables(settingsPath))
        {
            // Каталог самого исполняемого файла (…\bin).
            var bin = Path.GetDirectoryName(exe);
            if (string.IsNullOrWhiteSpace(bin))
                continue;

            candidates.Add(bin);
            // Родитель каталога bin — это каталог версии (если есть).
            var versionDir = Path.GetDirectoryName(bin);
            if (string.IsNullOrWhiteSpace(versionDir))
                continue;

            candidates.Add(versionDir);
            // Над каталогом версии обычно лежит корень 1cv8.
            var root = Path.GetDirectoryName(versionDir);
            if (!string.IsNullOrWhiteSpace(root)
                && string.Equals(Path.GetFileName(root), "1cv8", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(root);
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var full = dir.Trim();
            if (seen.Add(full) && Directory.Exists(full))
                target.Add(full);
        }
    }

    /// <summary>
    /// Возвращает пути к исполняемым файлам платформы 1С, заданные в settings.cnf.
    /// StartManager 1.4 хранит этот файл в XML: пути лежат в разделе «Launch»,
    /// в элементах V81AppFile…V84AppFile. Разбор INI с ключом V8AppPath оставлен
    /// запасным вариантом для файлов другого вида.
    /// </summary>
    private static IEnumerable<string> ReadPlatformExecutables(string settingsPath)
    {
        var result = new List<string>();

        if (IsXmlFile(settingsPath))
        {
            try
            {
                var document = XDocument.Load(settingsPath);
                result.AddRange(document
                    .Descendants()
                    .Where(e => e.Name.LocalName.EndsWith("AppFile", StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Value.Trim())
                    .Where(v => v.Length > 0));
            }
            catch (Exception ex)
            {
                LogInfo($"Не удалось разобрать {Path.GetFileName(settingsPath)} как XML: {ex.Message}");
            }

            return result;
        }

        var values = ParseIniGlobalValues(settingsPath);
        if (values.TryGetValue(KV8AppPath, out var exePath) && !string.IsNullOrWhiteSpace(exePath))
            result.Add(exePath.Trim());

        return result;
    }

    /// <summary>
    /// Проверяет в settings.cnf признак нового метода шифрования паролей
    /// (<c>NewMethodEncryption</c>). Алгоритм этого метода неизвестен, поэтому пароли
    /// при нём не переносятся. Для файла, который не читается, считаем метод обычным:
    /// иначе пароли терялись бы у всех, у кого файла настроек нет.
    /// </summary>
    private static bool UsesUnknownPasswordEncryption(string settingsPath)
    {
        if (!File.Exists(settingsPath) || !IsXmlFile(settingsPath))
            return false;

        try
        {
            var value = XDocument.Load(settingsPath)
                .Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, KNewEncryption, StringComparison.OrdinalIgnoreCase))
                ?.Value;
            return !string.IsNullOrWhiteSpace(value) && IsTrue(value);
        }
        catch (Exception ex)
        {
            LogInfo($"Не удалось прочитать признак {KNewEncryption} из {Path.GetFileName(settingsPath)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Определяет по первому значимому символу, является ли файл XML-документом.</summary>
    private static bool IsXmlFile(string path)
    {
        try
        {
            foreach (var line in ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    continue;
                return trimmed[0] == '<';
            }
        }
        catch (IOException)
        {
            // Нечитаемый файл разбираем как INI: там ошибка обрабатывается тише.
        }

        return false;
    }

    // ---------------------------------------------------------------- разбор файлов

    /// <summary>Разбирает v8config.smc на список секций: [Имя] → словарь ключ/значение.</summary>
    private static List<Dictionary<string, string>> ParseSmcFile(string path)
    {
        var sections = new List<Dictionary<string, string>>();
        Dictionary<string, string>? current = null;

        foreach (var rawLine in ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Сохраняем имя секции под служебным ключом.
                current["__section__"] = line.Substring(1, line.Length - 2).Trim();
                sections.Add(current);
                continue;
            }

            if (current is null)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim();
            current[key] = value;
        }

        return sections;
    }

    /// <summary>Разбирает простой INI-файл глобальных настроек (без секций) в словарь.</summary>
    private static Dictionary<string, string> ParseIniGlobalValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line.Substring(0, eq).Trim();
            var value = line.Substring(eq + 1).Trim().Trim('"');
            values[key] = value;
        }

        return values;
    }

    /// <summary>
    /// Читает все строки файла настроек StartManager. Кодировка определяется по BOM:
    /// StartManager 1.4 пишет свои файлы в UTF-8 с BOM, а более старые сборки — в ANSI
    /// (Windows-1251), поэтому ANSI остаётся кодировкой по умолчанию для файлов без BOM.
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        using var reader = new StreamReader(path, Ansi, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
    }

    // ---------------------------------------------------------------- расшифровка пароля

    /// <summary>
    /// Расшифровывает пароль StartManager. Метод Виженера по однобайтовым кодам
    /// Windows-1251 с ключом «SLAVKA240601»: каждый байт шифротекста смещается назад
    /// на код соответствующего символа ключа и вперёд на код символа «0» (по модулю 256).
    /// Пустые значения возвращаются без изменений. Смещение на 48 проверено на паролях
    /// «123» (шифр «TND») и «Abc-99» (шифр «d~tSTJ»): без него расшифровка промахивается
    /// ровно на код нуля.
    /// </summary>
    /// <remarks>
    /// Шифр работает по однобайтовым кодам независимо от того, ASCII ли это или кириллица
    /// (в Windows-1251 кириллица — тоже один байт). Поэтому байты шифротекста старше 0x7F —
    /// нормальное следствие сдвига и у обычных паролей (например, пароль «abcdefghijklmnop»
    /// даёт шифробайты до 0x96, см. правку ключа шифра), а не признак другого метода.
    /// Раньше такие пароли здесь отвергались и после импорта получались пустыми
    /// (issue #163); теперь расшифровываются всегда. Корректность кириллицы подтверждена
    /// сквозной проверкой шифрования/расшифровки «Пароль1» при кодировке 1251 (issue #163).
    /// </remarks>
    public static string DecryptPassword(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;

        var data = Ansi.GetBytes(encrypted);

        var key = Encoding.ASCII.GetBytes(VigenereKey);
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var shift = key[i % key.Length];
            result[i] = (byte)((data[i] - shift + ZeroDigit) & 0xFF);
        }

        return Ansi.GetString(result);
    }

    /// <summary>
    /// Шифрует пароль методом Виженера с ключом «SLAVKA240601» (обратная операция к
    /// <see cref="DecryptPassword"/>). Используется для тестов и отладки.
    /// </summary>
    public static string EncryptPassword(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
            return string.Empty;

        var data = Ansi.GetBytes(plain);
        var key = Encoding.ASCII.GetBytes(VigenereKey);
        var result = new byte[data.Length];

        for (var i = 0; i < data.Length; i++)
        {
            var shift = key[i % key.Length];
            result[i] = (byte)((data[i] + shift - ZeroDigit) & 0xFF);
        }

        return Ansi.GetString(result);
    }

    // ---------------------------------------------------------------- вспомогательное

    private static string? Get(Dictionary<string, string> section, string key)
        => section.TryGetValue(key, out var value) ? value : null;

    /// <summary>Возвращает имя секции: в v8config.smc это идентификатор базы из списка 1С.</summary>
    private static string SectionName(Dictionary<string, string> section)
        => Get(section, "__section__") ?? string.Empty;

    private static bool HasTrue(Dictionary<string, string> section, string key)
    {
        var value = Get(section, key);
        return !string.IsNullOrWhiteSpace(value) && IsTrue(value);
    }

    private static bool IsTrue(string value)
    {
        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => bool.TryParse(value.Trim(), out var b) && b
        };
    }

    /// <summary>Нормализует путь группы к внутреннему разделителю приложения « / ».</summary>
    private static string NormalizeGroupPath(string? group)
    {
        var segments = SplitGroupPath(group ?? string.Empty);
        return string.Join(GroupHierarchyHelper.PathSeparator, segments);
    }

    private static List<string> SplitGroupPath(string path)
    {
        return path
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}