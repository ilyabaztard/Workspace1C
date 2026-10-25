using System.Collections.Generic;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (часть B): разбор директив Include/IncludeOptional конфигурации Apache.
/// Чистая функция <see cref="ApacheConfIncludeParser.ParseIncludeTargets"/> —
/// тест не зависит от платформы и реальных файлов.
/// </summary>
public sealed class ApacheConfIncludeParserTests
{
    [Fact]
    public void ApacheConfIncludeParser_ParsesIncludeAndIncludeOptional_WithRelativePaths()
    {
        const string confText = @"
# основной конфиг
ServerRoot ""C:\Apache24""
Listen 80

Include extra/httpd-vhosts.conf
IncludeOptional ""extra/mpm/*.conf""
Include /apache/ssl/extraSSL.conf
IncludeOptional missing.conf
";
        var targets = ApacheConfIncludeParser.ParseIncludeTargets(confText, @"C:\Apache24\conf");

        Assert.Contains(@"C:\Apache24\conf\extra\httpd-vhosts.conf", targets);
        // Относительный путь с wildcard: помечается каталогом, хранится как есть.
        Assert.Contains(@"C:\Apache24\conf\extra\mpm\*.conf", targets);
        // Абсолютный путь не трогаем.
        Assert.Contains(@"/apache/ssl/extraSSL.conf", targets);
        Assert.DoesNotContain("missing.conf", targets, System.StringComparer.Ordinal);
        Assert.Equal(4, targets.Count);
    }

    [Fact]
    public void ApacheConfIncludeParser_IgnoresCommentsAndNonIncludeDirectives()
    {
        const string confText = @"
Listen 80
LoadModule ssl_module modules/mod_ssl.so
# Include commented.conf
IncludeOptional built_in.conf
";
        var targets = ApacheConfIncludeParser.ParseIncludeTargets(confText, "/etc/apache2");
        Assert.Equal(new[] { "/etc/apache2/built_in.conf" }, targets);
    }

    [Fact]
    public void ApacheConfIncludeParser_EmptyOrNullText_ReturnsEmpty()
    {
        Assert.Empty(ApacheConfIncludeParser.ParseIncludeTargets(string.Empty, "/etc/apache2"));
        Assert.Empty(ApacheConfIncludeParser.ParseIncludeTargets(null!, "/etc/apache2"));
    }

    [Fact]
    public void ApacheConfIncludeParser_IsInclude_MatchesCaseInsensitively()
    {
        Assert.True(ApacheConfIncludeParser.IsInclude("Include"));
        Assert.True(ApacheConfIncludeParser.IsInclude("includeoptional"));
        Assert.True(ApacheConfIncludeParser.IsInclude("INCLUDE"));
        Assert.False(ApacheConfIncludeParser.IsInclude("LoadModule"));
    }

    // ------------------- Define и ${VAR} (issue #63, замечание 7OH) -------------------

    [Fact]
    public void ParseDefines_ReadsNameAndQuotedValue_CaseInsensitiveDirective()
    {
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        define OneCPort 8027
        Define EMPTY ""
        LoadModule foo modules/mod_foo.so
        """;

        var defines = ApacheConfIncludeParser.ParseDefines(conf);

        Assert.Equal("c:/Apache/Apache24", defines["SRVROOT"]);
        Assert.Equal("8027", defines["OneCPort"]);
        Assert.Equal(3, defines.Count); // LoadModule не директива Define
    }

    [Fact]
    public void ParseDefines_LaterDefinitionOverwrites()
    {
        var defines = ApacheConfIncludeParser.ParseDefines("Define Var one\nDefine Var two");

        Assert.Equal("two", defines["Var"]);
    }

    [Fact]
    public void ExpandVariables_SubstitutesKnownAndKeepsUnknown()
    {
        var defines = ApacheConfIncludeParser.ParseDefines(
            "Define SRVROOT \"c:/Apache/Apache24\"\nDefine OneCPort 8027");

        var expanded = ApacheConfIncludeParser.ExpandVariables(
            "${SRVROOT}/htdocs/${OneCPort}/demo_27_file", defines);

        Assert.Equal("c:/Apache/Apache24/htdocs/8027/demo_27_file", expanded);
        // Неизвестная переменная остаётся литералом — разбор не падает.
        Assert.Equal("${NOPE}/x", ApacheConfIncludeParser.ExpandVariables("${NOPE}/x", defines));
    }

    [Fact]
    public void ExpandVariables_CaseInsensitiveName_AndEmptyDefines()
    {
        var defines = ApacheConfIncludeParser.ParseDefines("Define SRVROOT c:/Apache");

        Assert.Equal("c:/Apache/x", ApacheConfIncludeParser.ExpandVariables("${srvroot}/x", defines));
        Assert.Equal("${SRVROOT}/x", ApacheConfIncludeParser.ExpandVariables("${SRVROOT}/x", null));
    }

    [Fact]
    public void ParseDocumentRoot_ExpandsDefineVariables()
    {
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        Define OneCPort "8027"
        DocumentRoot "${SRVROOT}/htdocs/${OneCPort}/demo_27_file"
        """;

        var doc = ApacheConfIncludeParser.ParseDocumentRoot(conf, "c:/Apache/Apache24/conf");

        // Путь резолвится через Path.GetFullPath — на Windows слеши становятся
        // обратными, поэтому сравниваем нормализованно.
        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\8027\demo_27_file"), doc);
    }

    [Fact]
    public void ParseListenPorts_ExpandsDefineVariable()
    {
        const string conf = """
        Define OneCPort 8027
        Listen 80
        Listen 0.0.0.0:${OneCPort}
        Listen [::]:8080
        """;

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Equal(new[] { 80, 8027, 8080 }, ports);
    }

    [Fact]
    public void ParseListenPorts_WithoutDefines_UnresolvedVariableIsSkipped()
    {
        // Без двигателя Define порт из ${...} не извлекается — конфиг без Define
        // сохраняет прежнее поведение (не падает, порт 80 всё равно отдаёт).
        var ports = ApacheConfIncludeParser.ParseListenPorts("Listen 80\nListen 0.0.0.0:${OneCPort}");

        Assert.Equal(new[] { 80 }, ports);
    }
}