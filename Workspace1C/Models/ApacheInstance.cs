using System.Collections.Generic;
using Workspace1C.Services;

namespace Workspace1C.Models;

/// <summary>
/// Установленный инстанс веб-сервера Apache (issue #63): служба/процесс, корневой
/// каталог, файл конфигурации и включаемые файлы. Используется окном
/// «Публикация на веб-сервере» для автозаполнения конфигурации webinst.
/// </summary>
public sealed class ApacheInstance
{
    /// <summary>Идентификатор: имя службы (Windows) или имя процесса/версия (Linux).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Короткое имя: служба httpd (Windows) или «Apache» (Linux).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание: DisplayName/Description службы или строка версии.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Файл конфигурации Apache (httpd.conf / apache2.conf) — основной.</summary>
    public string ConfPath { get; set; } = string.Empty;

    /// <summary>Корневой каталог Apache (ServerRoot).</summary>
    public string ServerRoot { get; set; } = string.Empty;

    /// <summary>Каталог публикации по умолчанию (htdocs / DocumentRoot).</summary>
    public string Htdocs { get; set; } = string.Empty;

    /// <summary>Вид инстанса (Apache 2.4 / Apache 2.2) для webinst.</summary>
    public WebServerKind Kind { get; set; } = WebServerKind.Apache24;

    /// <summary>Файлы, подключаемые директивами Include/IncludeOptional из конфига (абсолютные пути).</summary>
    public IReadOnlyList<string> IncludeFiles { get; set; } = System.Array.Empty<string>();

    /// <summary>Каталоги-маски Include с wildcard «*» (абсолютные пути).</summary>
    public IReadOnlyList<string> IncludeDirs { get; set; } = System.Array.Empty<string>();
}