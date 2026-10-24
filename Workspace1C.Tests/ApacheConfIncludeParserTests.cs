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
}