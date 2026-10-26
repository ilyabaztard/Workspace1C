using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 «Доработка публикации баз» (последний комментарий 7OH от 2026-10-25,
/// источник требований): реальные конфиги пользователя из архива 27_conf.zip
/// (httpd27.conf — главный файл запуска через <c>-f conf\httpd27.conf</c>, Include
/// conf/httpd.conf, Define SRVROOT/OneCPort, DocumentRoot/Listen в httpd.conf,
/// второй уровень Include — 1c8Aliases27.conf) зафиксированы как фикстуры в
/// TestData/Apache27Conf и воспроизводят сценарий «Пока не удалось»: относительный
/// конфиг из ImagePath резолвится от ServerRoot, Include-граф раскрывается по всему
/// дереву, каталог публикации и порт Listen подставляются в окно публикации.
/// </summary>
public sealed class Apache27ConfRegressionTests
{
    private const string ServerRootDir = @"C:\Apache\Apache24";

    private static string TestDataDir => Path.Combine(FindRepoRoot(), "Workspace1C.Tests", "TestData", "Apache27Conf");

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(TestDataDir, fileName));

    /// <summary>
    /// Чтение включаемых файлов по имени (пути в конфигах пользователя — от
    /// <c>C:\Apache\Apache24</c>): файлы, отсутствующие в фикстурах
    /// (conf/extra/*.conf), не роняют разбор.
    /// </summary>
    private static string? ReadFixtureByPath(string path)
    {
        var fixture = Path.Combine(TestDataDir, Path.GetFileName(path));
        return File.Exists(fixture) ? File.ReadAllText(fixture) : null;
    }

    [Fact]
    public void Windows_ImagePath_RelativeConf_IsResolvedAgainstServerRoot()
    {
        // Строка запуска из комментария: -d c:\Apache\Apache24 -f conf\httpd27.conf.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache\Apache24", instance!.ServerRoot, ignoreCase: true);
        // Относительный -f резолвится от ServerRoot: conf\httpd27.conf → C:\Apache\Apache24\conf\httpd27.conf.
        Assert.Equal(@"C:\Apache\Apache24\conf\httpd27.conf", instance.ConfPath, ignoreCase: true);
        Assert.Equal(@"C:\Apache\Apache24\htdocs", instance.Htdocs, ignoreCase: true);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
    }

    [Fact]
    public void PopulateIncludes_RealUserConfigs_ResolvesDocumentRootAndListenAcrossGraph()
    {
        // Главный файл запуска — httpd27.conf (передаётся через -f conf\httpd27.conf),
        // а не httpd.conf. ServerRoot из -d c:\Apache\Apache24.
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };

        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        // DocumentRoot "${SRVROOT}/htdocs/${OneCPort}" = c:/Apache/Apache24/htdocs/27:
        // Define OneCPort — в главном httpd27.conf, Define SRVROOT — во включаемом
        // httpd.conf; общий словарь по всему графу раскрыл путь.
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), instance.DocumentRoot, ignoreCase: true);

        // Listen "80${OneCPort}" = 8027 и Listen "192.168.2.11:80${OneCPort}" = 192.168.2.11:8027.
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);

        // Include-граф раскрыт рекурсивно по обоим уровням: conf/httpd.conf (уровень 1)
        // и ${SRVROOT}/conf/1c8Aliases27.conf (уровень 2, из httpd.conf).
        Assert.Contains(instance.IncludeFiles, p => p.EndsWith("httpd.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(instance.IncludeFiles, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PopulateIncludes_RelativeInclude_IsResolvedFromServerRoot()
    {
        // «Include conf/httpd.conf» из conf\httpd27.conf Apache трактует от ServerRoot
        // (C:\Apache\Apache24), а не от каталога включающего файла (C:\Apache\Apache24\conf).
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Kind = WebServerKind.Apache24
        };

        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        Assert.Contains(instance.IncludeFiles, p => string.Equals(
            Path.GetFullPath(@"C:\Apache\Apache24\conf\httpd.conf"), p, System.StringComparison.OrdinalIgnoreCase));
        // Ошибочного «conf\conf\httpd.conf» (от каталога включающего файла) быть не должно.
        Assert.DoesNotContain(instance.IncludeFiles, p =>
            p.Contains("conf" + Path.DirectorySeparatorChar + "conf" + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ViewModel_RealUserConfigs_FillsPhysicalDirAndUrlWithPort()
    {
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Каталог публикации = DocumentRoot из конфига + имя виртуального каталога.
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"),
            vm.PhysicalDirectory, ignoreCase: true);
        // URL учитывает порт Listen (8027), а не «сырое» значение без порта.
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
        Assert.Equal(WebServerKind.Apache24, vm.BuildRequest().Server);
    }

    [Fact]
    public void ViewModel_RealUserConfigs_ConfTargetsContainAliasesFile()
    {
        // Issue #63, последний комментарий: «пропала опция … файла алиасов выборе нет,
        // а значения по умолчанию из обычного файла, а не который указан в службе».
        // Основной конфиг — httpd27.conf из службы (-f conf\httpd27.conf), а не «обычный»
        // httpd.conf; второй ComboBox webinst показывает include-файлы графа, включая
        // 1c8Aliases27.conf.
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = Path.Combine(ServerRootDir, "conf", "httpd27.conf"),
            ServerRoot = ServerRootDir,
            Htdocs = Path.Combine(ServerRootDir, "htdocs"),
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, ReadFixture("httpd27.conf"),
            Path.Combine(ServerRootDir, "conf"), ReadFixtureByPath);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);

        Assert.True(vm.HasConfTargets);
        // Второй ComboBox webinst: основной конфиг службы и включаемые файлы графа
        // (в т.ч. файл алиасов 1c8Aliases27.conf).
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("httpd27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.ConfTargetPaths, p => p.EndsWith("1c8Aliases27.conf", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файлы");
        // По умолчанию выбран основной конфиг — конфиг, указанный в службе.
        Assert.Equal(0, vm.ConfTargetIndex);
        Assert.Equal(Path.GetFullPath(Path.Combine(ServerRootDir, "conf", "httpd27.conf")),
            vm.ApacheConfPath, ignoreCase: true);
    }

    [Fact]
    public void PopulateIncludes_WildcardAliasesInclude_ResolvesAliasFileAndConfTargets()
    {
        // Issue #63 (последний комментарий 7OH от 2026-10-26: «файл алиаса по-прежнему не
        // вернулся»): include алиасов может идти через IncludeOptional с маской каталога
        // («IncludeOptional aliases/*.conf» — типовой способ подключения 1c8Aliases*.conf).
        // Детектор обязан раскрыть маску в реальные файлы (IncludeFiles), а VM — показать
        // пункт «Include: 1c8Aliases27.conf» в списке webinst.
        var tempDir = Path.Combine(Path.GetTempPath(), "W1C_Apache27Aliases_" + Guid.NewGuid().ToString("N"));
        try
        {
            var aliasesDir = Path.Combine(tempDir, "aliases");
            Directory.CreateDirectory(aliasesDir);
            File.WriteAllText(Path.Combine(aliasesDir, "1c8Aliases27.conf"),
                "# 1c publication\nAlias \"/Demo\" \"${SRVROOT}/htdocs/27/Demo/\"\n");
            File.WriteAllText(Path.Combine(aliasesDir, "ignored.txt"), "not a conf\n");
            var mainPath = Path.Combine(tempDir, "httpd27.conf");
            File.WriteAllText(mainPath,
                "Define SRVROOT \"" + tempDir.Replace("\\", "/") + "\"\r\n" +
                "Define OneCPort 27\r\n" +
                "IncludeOptional aliases/*.conf\r\n");

            var instance = new ApacheInstance
            {
                Id = "httpd27",
                Name = "Apache 2.4 (httpd-27)",
                ConfPath = mainPath,
                ServerRoot = tempDir,
                Htdocs = Path.Combine(tempDir, "htdocs"),
                Kind = WebServerKind.Apache24
            };
            ApacheInstanceDetector.PopulateIncludes(instance, File.ReadAllText(mainPath), tempDir);

            // Маска раскрыта в реальный файл алиасов (aliases\1c8Aliases27.conf) —
            // файл в IncludeFiles, а не только «шаблон» каталога в IncludeDirs.
            Assert.Contains(instance.IncludeFiles, p =>
                p.EndsWith(Path.Combine("aliases", "1c8Aliases27.conf"), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(instance.IncludeFiles, p => p.EndsWith("ignored.txt", StringComparison.OrdinalIgnoreCase));

            var infobase = new Infobase
            {
                Name = "Тестовая база",
                Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
            };
            var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
            vm.ServerIndex = vm.ServerIndexFor(instance);

            // Список webinst: основной конфиг службы + include-файл алиасов.
            Assert.True(vm.HasConfTargets);
            Assert.Contains(vm.ConfTargetPaths, p =>
                p.EndsWith(Path.Combine("aliases", "1c8Aliases27.conf"), StringComparison.OrdinalIgnoreCase));
            Assert.True(vm.ConfTargets.Count >= 2, "В списке webinst должны быть основной конфиг и include-файл алиасов");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* временный каталог не влияет на результат теста */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }
}
