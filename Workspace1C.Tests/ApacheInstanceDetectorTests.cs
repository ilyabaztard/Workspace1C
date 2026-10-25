using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (часть B): обнаружение инстансов Apache. Чистые функции разбора
/// командной строки службы (Windows) и вывода apache2ctl -V/-S (Linux)
/// тестируются без реальных служб/процессов.
/// </summary>
public sealed class ApacheInstanceDetectorTests
{
    [Fact]
    public void ApacheInstanceDetector_Windows_ParsesServiceImagePath_DAndF_Switches()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "Apache2.4", "Apache HTTP Server", "Apache/2.4.62",
            "\"C:\\Apache24\\bin\\httpd.exe\" -d \"C:\\Apache24\" -f \"C:\\Apache24\\conf\\httpd.conf\"");

        Assert.NotNull(instance);
        Assert.Equal("Apache2.4", instance!.Id);
        Assert.Equal(@"C:\Apache24", instance.ServerRoot);
        Assert.Equal(@"C:\Apache24\conf\httpd.conf", instance.ConfPath);
        Assert.Equal(@"C:\Apache24\htdocs", instance.Htdocs);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_WithoutDOrF_UsesExeDirAndDefaultConf()
    {
        // Нет ключей -d/-f: ServerRoot выводится из каталога бинарника, conf — по умолчанию.
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "httpd", "Apache", "Apache 2.2",
            "\"C:\\Apache22\\bin\\httpd.exe\" -k runservice");

        Assert.NotNull(instance);
        Assert.Equal(@"C:\Apache22", instance!.ServerRoot);
        Assert.Equal(@"C:\Apache22\conf\httpd.conf", instance.ConfPath);
        Assert.Equal(WebServerKind.Apache22, instance.Kind);
    }

    [Fact]
    public void ApacheInstanceDetector_Windows_NoApacheSwitches_ReturnsNull()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            "some-service", "Some Service", "Not Apache", null);
        Assert.Null(instance);
    }

    [Fact]
    public void ApacheInstanceDetector_Linux_ParsesApacheCtl_V_ServerRootAndConfig()
    {
        const string apacheCtl = @"Server version: Apache/2.4.62 (Ubuntu)
Server built:   2024-08-07T13:24:45
Server's Module Magic Number: 20120211:129
Server loaded:  APR 1.5.2, APR-UTIL 1.5.4
Compiled using: APR 1.5.2, APR-UTIL 1.5.4
 -D HTTPD_ROOT=""/etc/apache2""
 -D SERVER_CONFIG_FILE=""conf/httpd.conf""
";
        const string apacheCtlS = @"VirtualHost configuration:
wildcard NameVirtualHosts and _default_ servers:
*:80                   is a NameVirtualHost
         default server localhost (/etc/apache2/sites-enabled/000-default.conf:1)
Main DocumentRoot: ""/var/www/html""
";

        var instance = ApacheInstanceDetector.ParseApacheCtl(
            apacheCtl, apacheCtlS, "/etc/apache2/apache2.conf", "/var/www");

        Assert.NotNull(instance);
        // Относительный SERVER_CONFIG_FILE резолвится против HTTPD_ROOT.
        Assert.Equal("/etc/apache2/conf/httpd.conf", instance!.ConfPath);
        Assert.Equal("/etc/apache2", instance.ServerRoot);
        Assert.Equal("/var/www/html", instance.Htdocs);
        Assert.Equal(WebServerKind.Apache24, instance.Kind);
        Assert.Contains("Apache/2.4.62", instance.Name);
    }

    [Fact]
    public void ApacheInstanceDetector_Linux_ParsesAbsoluteServerConfigFileAndFallsBackHtdocs()
    {
        const string apacheCtl = @"Server version: Apache/2.4.6 (CentOS)
 -D HTTPD_ROOT=""/etc/httpd""
 -D SERVER_CONFIG_FILE=""/etc/httpd/conf/httpd.conf""
";
        var instance = ApacheInstanceDetector.ParseApacheCtl(
            apacheCtl, null, "/etc/httpd/conf/httpd.conf", "/var/www");

        Assert.NotNull(instance);
        Assert.Equal("/etc/httpd/conf/httpd.conf", instance!.ConfPath);
        Assert.Equal("/etc/httpd", instance.ServerRoot);
        // Нет вывода -S: подставляется каталог htdocs по умолчанию.
        Assert.Equal("/var/www", instance.Htdocs);
    }

    [Fact]
    public void ApacheInstanceDetector_LooksLikeApache_MatchesHttpdAndApacheCaseInsensitive()
    {
        Assert.True(ApacheInstanceDetector.LooksLikeApache("Apache HTTP Server"));
        Assert.True(ApacheInstanceDetector.LooksLikeApache("httpd"));
        Assert.True(ApacheInstanceDetector.LooksLikeApache("Apache/2.4.62 (Ubuntu)"));
        Assert.False(ApacheInstanceDetector.LooksLikeApache("nginx"));
        Assert.False(ApacheInstanceDetector.LooksLikeApache(null));
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_SplitsFilesAndWildcardDirs()
    {
        var instance = new ApacheInstance { ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include extra/httpd-vhosts.conf\nIncludeOptional \"extra/sites/*.conf\"\n",
            @"C:\apache\conf");

        Assert.Single(instance.IncludeFiles);
        Assert.Equal(@"C:\apache\conf\extra\httpd-vhosts.conf", instance.IncludeFiles[0]);
        Assert.Contains(@"C:\apache\conf\extra\sites\*.conf", instance.IncludeDirs);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_ExpandsDefineVariables_Issue63()
    {
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort "8027"
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 80
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance, conf, @"C:\apache\conf");

        // Переменные из файла раскрыты («а переменные из файла кто подставлять будет сейчас?»).
        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Contains(8027, instance.ListenPorts);
        Assert.Contains(80, instance.ListenPorts);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_WithoutDefines_KeepsDocumentRootAndPort80()
    {
        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "DocumentRoot \"C:/apache/htdocs\"\nListen 80\n",
            @"C:\apache\conf");

        Assert.Equal(Path.GetFullPath(@"C:\apache\htdocs"), instance.DocumentRoot);
        Assert.Equal(new[] { 80 }, instance.ListenPorts);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_TwoPass_DefinesBeforeInclude_Issue63()
    {
        // issue #63, комментарий 7OH: «Переменные могут быть объявлены до include» — Define
        // живут в главном httpd.conf, а DocumentRoot/Listen — во включаемом файле. Двухпроходный
        // разбор: Define главного конфига видны внутри include-файла → путь и порт раскрыты.
        const string mainConf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort 8027
        Include conf/extra/publish.conf
        """;
        const string includeConf = """
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        Listen 0.0.0.0:${OneCPort}
        """;

        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance, mainConf, @"C:\apache\conf",
            _ => includeConf);

        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), instance.DocumentRoot);
        Assert.Contains(8027, instance.ListenPorts);

        // Include-файл попал в IncludeFiles (маршрутизация ВТОРОГО прохода).
        Assert.Contains(@"C:\apache\conf\conf\extra\publish.conf", instance.IncludeFiles);
    }

    [Fact]
    public void ApacheInstanceDetector_PopulateIncludes_MissingIncludeFile_DoesNotFail()
    {
        // Включаемый файл отсутствует (нет на диске / readFileText вернул null) — разбор
        // главного конфига не падает, DocumentRoot/порты главного файла заполняются.
        var instance = new ApacheInstance { ServerRoot = @"C:\apache", ConfPath = @"C:\apache\httpd.conf" };
        ApacheInstanceDetector.PopulateIncludes(instance,
            "Include extra/httpd-vhosts.conf\nDocumentRoot \"C:/apache/htdocs\"\nListen 80\n",
            @"C:\apache\conf",
            _ => null);

        Assert.Equal(Path.GetFullPath(@"C:\apache\htdocs"), instance.DocumentRoot);
        Assert.Equal(new[] { 80 }, instance.ListenPorts);
        Assert.Single(instance.IncludeFiles);
    }
}