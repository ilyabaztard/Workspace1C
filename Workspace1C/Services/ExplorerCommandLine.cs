#if WINDOWS
using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Обработчик команд контекстного меню проводника (функция №12): параметры запуска
/// <c>--register "<путь>"</c>, <c>--launch "<путь>"</c> и
/// <c>--designer "<путь>"</c>. Вызывается из <c>App.OnStartup</c> после
/// инициализации контейнера зависимостей и активного профиля.
/// <para>
/// Логика: по пути к <c>.1CD</c> определяется каталог файловой базы, находится или
/// создаётся информационная база в списке приложения, затем база либо только
/// регистрируется, либо запускается («1С:Предприятие» / «Конфигуратор»).
/// </para>
/// </summary>
public static class ExplorerCommandLine
{
    /// <summary>
    /// Пытается обработать аргументы командной строки как команду из контекстного меню
    /// проводника. Возвращает <c>true</c>, если аргументы содержали такую команду
    /// (независимо от успеха действия), иначе <c>false</c>.
    /// </summary>
    public static bool TryHandle(string[]? args)
    {
        if (args is null || args.Length == 0)
            return false;

        var verb = ResolveVerb(args);
        if (verb is null)
            return false;

        var path = ResolvePath(args, verb);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var repository = AppServices.GetRequiredService<IInfobaseRepository>();
        var launcher = AppServices.GetRequiredService<IOneCLauncher>();
        var logger = AppServices.GetRequiredService<IAppLogger>();

        try
        {
            var baseDir = ResolveBaseDirectory(path);
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
            {
                logger.Warn($"[explorer] Каталог базы не найден: '{path}'");
                return true;
            }

            var bases = repository.Load();
            var infobase = bases.FirstOrDefault(b =>
                b.Connection?.Type == ConnectionType.File
                && PathsEqual(b.Connection.FilePath, baseDir));

            var added = false;
            if (infobase is null)
            {
                infobase = new Infobase
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = new DirectoryInfo(baseDir).Name,
                    Group = string.Empty,
                    Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = baseDir }
                };
                bases.Add(infobase);
                added = true;
            }

            if (verb == ExplorerIntegrationService.VerbRegister)
            {
                if (added)
                    repository.Save(bases);
                logger.Info($"[explorer] База зарегистрирована в списке: '{infobase.Name}'");
                return true;
            }

            // Launch / Designer: сохраняем базу (если новая) и запускаем.
            if (added)
                repository.Save(bases);

            var mode = verb == ExplorerIntegrationService.VerbDesigner
                ? OneCLaunchMode.Configurator
                : OneCLaunchMode.Enterprise;
            var ok = launcher.Launch(infobase, mode);
            // issue #80 «Настройка логирования»: запуск баз — направление CM_BASE_RUN.
            Log.Info(LogCategory.BaseRun, $"[explorer] {(ok ? "Запущена" : "Не удалось запустить")} база '{infobase.Name}' ({mode})");
            return true;
        }
        catch (Exception ex)
        {
            try { logger.Error("[explorer] Ошибка обработки команды проводника: " + ex); }
            catch { /* логирование не должно маскировать ошибку */ }
            return true;
        }
    }

    /// <summary>Определяет команду из аргументов: register/launch/designer, либо null.</summary>
    private static string? ResolveVerb(string[] args)
    {
        foreach (var arg in args)
        {
            var key = arg.Split('=')[0].Trim();
            if (string.Equals(key, "--register", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "--launch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "--designer", StringComparison.OrdinalIgnoreCase))
                return key;
        }
        return null;
    }

    /// <summary>Извлекает путь из аргументов для заданной команды (форма <c>--key value</c> или <c>--key=value</c>).</summary>
    private static string? ResolvePath(string[] args, string verb)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var eq = arg.IndexOf('=');
            var key = eq >= 0 ? arg.Substring(0, eq) : arg;
            if (!string.Equals(key.Trim(), verb, StringComparison.OrdinalIgnoreCase))
                continue;

            if (eq >= 0)
                return arg.Substring(eq + 1).Trim().Trim('"');

            // Путь — следующий аргумент.
            if (i + 1 < args.Length)
                return args[i + 1].Trim().Trim('"');
            return null;
        }
        return null;
    }

    /// <summary>
    /// По пути к <c>.1CD</c> (или каталогу) определяет каталог файловой базы.
    /// Если путь указывает на файл с расширением <c>.1CD</c> — возвращает его каталог,
    /// иначе считает путь каталогом базы.
    /// </summary>
    private static string? ResolveBaseDirectory(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (string.Equals(Path.GetExtension(full), ".1CD", StringComparison.OrdinalIgnoreCase))
                return Path.GetDirectoryName(full);
            return full;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Сравнивает два пути к каталогам без учёта регистра и конечного разделителя.</summary>
    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        try
        {
            var na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif