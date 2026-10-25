using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 «Доработка публикации баз» (комментарий 7OH от 2026-10-25): пункт
/// «Публикация на веб-сервере» перенесён из «Утилиты» в контекстное меню базы
/// («Администрирование», выше истории запусков); парсер конфигов Apache понимает
/// DocumentRoot (с подкаталогом сайта) и Listen; URL публикации учитывает порт.
/// </summary>
public sealed class WebPublishIssue63Tests
{
    // ---------- Перенос пункта в контекстное меню ----------

    [Fact]
    public void UtilitiesMenu_DoesNotContainWebPublish()
    {
        Assert.Null(UtilitiesMenuCatalog.Find("WebPublish"));

        var layout = UtilitiesMenuLayouts.Default();
        var ids = new HashSet<string>(System.StringComparer.Ordinal);
        UtilitiesMenuResolver.CollectItemIds(layout.Root, ids);
        Assert.DoesNotContain("WebPublish", ids);
    }

    [Fact]
    public void ContextMenu_CatalogContainsWebPublish_InAdministration()
    {
        var entry = ContextMenuCatalog.Find("WebPublish");

        Assert.NotNull(entry);
        Assert.Equal(ContextMenuCommandKind.WebPublish, entry!.CommandKind);
        Assert.Equal(ContextMenuCatalog.GroupAdministration, entry.DefaultGroup);
    }

    [Fact]
    public void ContextMenu_DefaultLayout_WebPublishAboveLaunchHistory()
    {
        var layout = ContextMenuLayouts.Default();
        var ids = CollectOrdered(layout.Root);

        Assert.Contains("WebPublish", ids);
        Assert.True(ids.IndexOf("WebPublish") < ids.IndexOf("LaunchHistory"),
            "Публикация должна идти выше «Истории запусков».");
    }

    /// <summary>Собирает ItemId узлов компоновки в порядке дерева (для проверки порядка).</summary>
    private static List<string> CollectOrdered(IEnumerable<ContextMenuNode> nodes)
    {
        var result = new List<string>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            if (string.Equals(node.Kind, "item", System.StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(node.ItemId))
                result.Add(node.ItemId);
            result.AddRange(CollectOrdered(node.Children ?? new List<ContextMenuNode>()));
        }
        return result;
    }

    // ---------- Парсер DocumentRoot / Listen ----------

    [Fact]
    public void Parser_ParsesDocumentRoot_RelativeToConfDir()
    {
        const string conf = "# config\nServerRoot \"C:/Apache/Apache24\"\nDocumentRoot \"htdocs/27\"\nListen 8027\n";

        var documentRoot = ApacheConfIncludeParser.ParseDocumentRoot(conf, @"C:\Apache\Apache24");

        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27"), documentRoot);
    }

    [Fact]
    public void Parser_ParsesDocumentRoot_Absolute_KeepsPath()
    {
        const string conf = "DocumentRoot \"C:/Apache/Apache24/htdocs/27\"\n";

        var documentRoot = ApacheConfIncludeParser.ParseDocumentRoot(conf, @"C:\Apache\Apache24");

        Assert.EndsWith(@"htdocs\27", Path.GetFullPath(documentRoot!));
    }

    [Fact]
    public void Parser_ParsesListenPorts_WithAddresses()
    {
        const string conf = "Listen 80\nListen 0.0.0.0:8027\nListen [::]:8080\n";

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Equal(new[] { 80, 8027, 8080 }, ports);
    }

    [Fact]
    public void Parser_ListenPorts_NoDuplicates()
    {
        const string conf = "Listen 80\nListen 0.0.0.0:80\n";

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Single(ports);
        Assert.Equal(80, ports[0]);
    }

    // ---------- URL с портом ----------

    [Fact]
    public void LocalUrl_Port80_NoPortInUrl()
    {
        Assert.Equal("http://localhost/demo_27/", WebPublishService.LocalUrl("demo_27", 80));
    }

    [Fact]
    public void LocalUrl_CustomListenPort_IncludesPort()
    {
        Assert.Equal("http://localhost:8027/demo_27/", WebPublishService.LocalUrl("demo_27", 8027));
    }

    [Fact]
    public void LocalUrl_NoPortParameter_UsesNoPort()
    {
        Assert.Equal("http://localhost/demo_27/", WebPublishService.LocalUrl("demo_27"));
    }

    // ---------- WebPublishViewModel: DocumentRoot и порт из инстанса ----------

    [Fact]
    public void ViewModel_PhysicalDirectoryAndUrl_UseInstanceDocumentRootAndListenPort()
    {
        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = @"C:\Apache\Apache24\conf\httpd.conf",
            ServerRoot = @"C:\Apache\Apache24",
            Htdocs = @"C:\Apache\Apache24\htdocs",
            DocumentRoot = @"C:\Apache\Apache24\htdocs\27",
            ListenPorts = new[] { 8027 },
            Kind = WebServerKind.Apache24
        };
        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.File,
                FilePath = @"D:\1C\test"
            }
        };

        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        // Выбираем инстанс Apache и имя виртуального каталога.
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\htdocs\27\demo_27"), vm.PhysicalDirectory);
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }

    [Fact]
    public void ViewModel_PopulatedFromDefineConfig_ResolvesDocumentRootAndPort_Issue63()
    {
        // Конфиг с директивами Define: парсер детектора раскрывает ${SRVROOT}/${OneCPort};
        // VM получает инстанс уже с раскрытыми значениями (замечание 7OH: «а переменные
        // из файла кто подставлять будет сейчас?», «Порт … так и не подставлен»).
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 8027
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance
        {
            Id = "httpd27",
            Name = "Apache 2.4 (httpd-27)",
            ConfPath = @"C:\Apache\Apache24\conf\httpd.conf",
            ServerRoot = @"C:\Apache\Apache24",
            Htdocs = @"C:\Apache\Apache24\htdocs",
            Kind = WebServerKind.Apache24
        };
        ApacheInstanceDetector.PopulateIncludes(instance, conf, @"C:\Apache\Apache24\conf");

        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Equal(new[] { 8027 }, instance.ListenPorts);

        var infobase = new Infobase
        {
            Name = "Тестовая база",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"D:\1C\test" }
        };
        var vm = new WebPublishViewModel(new[] { infobase }, infobase, new[] { instance });
        vm.ServerIndex = vm.ServerIndexFor(instance);
        vm.VirtualDirectory = "demo_27";

        // Регистр диска в конфиге пользователя — строчный («c:/Apache/…»), а в исходном
        // sibling-тесте — прописной: сравниваем без учёта регистра пути.
        Assert.True(string.Equals(
                vm.PhysicalDirectory,
                Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file\demo_27"),
                System.StringComparison.OrdinalIgnoreCase),
            $"Физический каталог не совпал: {vm.PhysicalDirectory}");
        Assert.Equal("http://localhost:8027/demo_27/", vm.Url);
    }
}