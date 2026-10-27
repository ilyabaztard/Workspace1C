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
    public void ParseListenPorts_ExpandsConcatenatedLiteralsAndVariable_80PlusOneCPort()
    {
        // Listen "80${OneCPort}" (реальные фикстуры TestData/Apache27Conf): кавычки
        // снимаются, литерал «80» и переменная OneCPort раскрываются вместе → 8027
        // (issue #63, компонент 7OH 2026-10-26: «порт тоже не подставился»).
        const string conf = """
        Define OneCPort 27
        Listen "80${OneCPort}"
        Listen "192.168.2.11:80${OneCPort}"
        """;

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Equal(new[] { 8027 }, ports);
    }

    [Fact]
    public void ParseListenPorts_WithoutDefines_UnresolvedVariableIsSkipped()
    {
        // Без двигателя Define порт из ${...} не извлекается — конфиг без Define
        // сохраняет прежнее поведение (не падает, порт 80 всё равно отдаёт).
        var ports = ApacheConfIncludeParser.ParseListenPorts("Listen 80\nListen 0.0.0.0:${OneCPort}");

        Assert.Equal(new[] { 80 }, ports);
    }

    [Fact]
    public void MergeDefines_CombinesTexts_LaterOverridesEarlier()
    {
        // Двухпроходный разбор (issue #63): Define из главного конфига + включаемых файлов
        // объединяются в один словарь; поздние файлы переопределяют ранние (как Apache).
        var merged = ApacheConfIncludeParser.MergeDefines(new[]
        {
            "Define SRVROOT \"c:/Apache/Apache24\"\nDefine OneCPort 8027",
            "Define OneCPort 8028\nDefine Extra \"x\""
        });

        Assert.Equal("c:/Apache/Apache24", merged["SRVROOT"]);
        Assert.Equal("8028", merged["OneCPort"]);   // поздний файл переопределяет
        Assert.Equal("x", merged["extra"]);          // ключи регистронезависимы
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void MergeDefines_EmptyAndNullTexts_AreSkipped()
    {
        var merged = ApacheConfIncludeParser.MergeDefines(new[] { string.Empty, "Define A 1", null! });

        Assert.Single(merged);
        Assert.Equal("1", merged["A"]);
    }

    [Fact]
    public void ParseIncludeTargets_ExpandsDefinesInIncludeTarget_AndResolvesFromConfDir()
    {
        // Include-цель с ${VAR} (типовой способ подключения файла алиасов 1С):
        // Define из того же текста раскрывается, относительный путь резолвится
        // от каталога конфигурации (issue #63).
        const string confText = """
        Define SRVROOT "C:/Apache/Apache24"
        IncludeOptional "${SRVROOT}/conf/1c8Aliases27.conf"
        """;

        var targets = ApacheConfIncludeParser.ParseIncludeTargets(confText, @"C:\Apache\Apache24\conf");

        Assert.Contains(Path.GetFullPath(@"C:\Apache\Apache24\conf\1c8Aliases27.conf"), targets);
        Assert.DoesNotContain("${", targets, System.StringComparer.Ordinal);
    }

    [Fact]
    public void ParseIncludeTargets_ExpandsVariablesInMiddleOfTarget()
    {
        // Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf" — литералы и переменная
        // в середине строки раскрываются вместе (реальные фикстуры TestData/Apache27Conf,
        // issue #63): путь без ${...} — это и есть используемый файл алиасов.
        const string confText = """
        Define SRVROOT "C:/Apache/Apache24"
        Define OneCPort 27
        Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"
        """;

        var targets = ApacheConfIncludeParser.ParseIncludeTargets(confText, @"C:\Apache\Apache24\conf");

        Assert.Single(targets);
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\conf\1c8Aliases27.conf"), targets[0]);
        Assert.DoesNotContain("${", targets, System.StringComparer.Ordinal);
    }

    // ---------- Реальные детали конфигов (issue #63, комментарий 7OH 2026-10-26):
    // комментарий «# …» в конце строки директивы и порядок Define/Include ----------

    [Fact]
    public void ParseDefines_InlineHashComment_IsStrippedFromValue()
    {
        // Реальные конфиги нередко поясняют Define комментарием в конце строки
        // («# порт инстанса»): без обрезки «27  # порт» не раскрывалось бы в путь/порт
        // (issue #63, «а переменные из файла кто подставлять будет сейчас?»).
        var defines = ApacheConfIncludeParser.ParseDefines(
            "Define SRVROOT \"c:/Apache/Apache24\"  # корень\r\n" +
            "Define OneCPort 27 # порт инстанса\r\n");

        Assert.Equal("c:/Apache/Apache24", defines["SRVROOT"]);
        Assert.Equal("27", defines["OneCPort"]);
    }

    [Fact]
    public void ParseListenPorts_InlineHashComment_DoesNotBreakPort()
    {
        // «Listen "80${OneCPort}"  # http-порт»: комментарий после значения не должен
        // попасть в порт (иначе int.TryParse не раскрыл бы 8027 и порт остался бы 80).
        const string conf = """
        Define OneCPort 27
        Listen "80${OneCPort}"  # http
        """;

        var ports = ApacheConfIncludeParser.ParseListenPorts(conf);

        Assert.Equal(new[] { 8027 }, ports);
    }

    [Fact]
    public void ParseDocumentRoot_InlineHashComment_DoesNotCorruptPath()
    {
        const string conf = """
        Define SRVROOT "c:/Apache/Apache24"
        DocumentRoot "${SRVROOT}/htdocs/27"  # каталог сайта
        """;

        var doc = ApacheConfIncludeParser.ParseDocumentRoot(conf, "c:/Apache/Apache24/conf");

        Assert.Equal(Path.GetFullPath(@"c:\Apache\Apache24\htdocs\27"), doc);
    }

    [Fact]
    public void ParseIncludeTargets_InlineHashComment_IsStrippedFromTarget()
    {
        // Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"  # алиасы 1С — комментарий
        // в конце include-строки не должен попадать в имя файла алиасов (иначе VM не
        // увидел бы используемый 1c8Aliases27.conf, а только «мусорное» имя с «# …»).
        const string confText = """
        Define SRVROOT "C:/Apache/Apache24"
        Define OneCPort 27
        Include "${SRVROOT}/conf/1c8Aliases${OneCPort}.conf"  # алиасы 1С
        """;

        var targets = ApacheConfIncludeParser.ParseIncludeTargets(confText, @"C:\Apache\Apache24\conf");

        Assert.Single(targets);
        Assert.Equal(Path.GetFullPath(@"C:\Apache\Apache24\conf\1c8Aliases27.conf"), targets[0]);
        Assert.DoesNotContain("#", targets[0], System.StringComparison.Ordinal);
    }
}