using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Workspace1C.Services;

/// <summary>
/// Результат проверки существования файла в WebDAV (п.9 «Синхронизация настроек»,
/// 1.0.0.91): <see cref="Exists"/> + метаданные для сравнения по времени изменения.
/// </summary>
public sealed class WebDavProbe
{
    /// <summary>Файл существует (HTTP 200/201 на HEAD/GET).</summary>
    public bool Exists { get; init; }

    /// <summary>Заголовок Last-Modified, нормализованный в UTC (может отсутствовать у серверов).</summary>
    public System.DateTime? LastModifiedUtc { get; init; }

    /// <summary>Размер файла (Content-Length), если сервер его вернул.</summary>
    public long? Length { get; init; }

    /// <summary>ETag (строка-детерминант версии), если сервер его вернул.</summary>
    public string? ETag { get; init; }
}

/// <summary>
/// WebDAV-клиент для двусторонней синхронизации настроек (п.9, 1.0.0.91): GET (скачивание),
/// HEAD (дата/размер/ETag для last-write-wins), PUT (загрузка) и MKCOL (создание каталогов).
/// Существующий <see cref="WebDavUploader"/> (DbmsBackupService) не изменяется: он продолжает
/// использоваться резервным копированием СУБД. Базовая аутентификация и обработка ошибок —
/// как в <see cref="WebDavUploader"/> (null при успехе, текст ошибки при сбое).
/// </summary>
public static class WebDavClient
{
    private static readonly HttpClient Http = new() { Timeout = System.TimeSpan.FromMinutes(5) };

    /// <summary>
    /// Склеивает базовый каталог WebDAV и относительный путь (слэши '/' во всех ОС).
    /// Каждый сегмент пути экранируется отдельно, чтобы не ломать вложенные каталоги.
    /// </summary>
    public static string CombineUrl(string baseUrl, string relativePath)
    {
        var segments = (relativePath ?? string.Empty)
            .Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        var joined = string.Join("/", segments);
        return baseUrl.TrimEnd('/') + (string.IsNullOrEmpty(joined) ? "/" : "/" + joined);
    }

    /// <summary>
    /// Проверка удалённого файла: HEAD, при отказе сервера — GET без чтения тела
    /// (заголовки Last-Modified / Content-Length / ETag). 404 → <see cref="WebDavProbe.Exists"/> = false.
    /// </summary>
    public static async Task<WebDavProbe> ProbeAsync(string fileUrl, string? user, string? password,
        CancellationToken ct = default)
    {
        try
        {
            var auth = BuildAuth(user, password);

            using (var head = new HttpRequestMessage(HttpMethod.Head, fileUrl))
            {
                head.Headers.Authorization = auth;
                using var headResponse = await Http.SendAsync(head, ct).ConfigureAwait(false);
                if (headResponse.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
                {
                    // Forbidden на HEAD у некоторых серверов — файл всё равно может существовать;
                    // 404 — файла нет. Пробуем GET-запасной путь только при 405/501/400.
                    if (headResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return new WebDavProbe { Exists = false };
                }

                if (headResponse.IsSuccessStatusCode)
                    return Parse(headResponse);
            }

            // Некоторые WebDAV-серверы (например, старые Яндекс.Диск-шлюзы) не поддерживают
            // HEAD — делаем GET и читаем только заголовки, тело не скачиваем.
            using (var get = new HttpRequestMessage(HttpMethod.Get, fileUrl))
            {
                get.Headers.Authorization = auth;
                get.Headers.Range = new RangeHeaderValue(0, 0);
                using var getResponse = await Http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (getResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return new WebDavProbe { Exists = false };
                if (getResponse.IsSuccessStatusCode)
                    return Parse(getResponse);
                return new WebDavProbe { Exists = false };
            }
        }
        catch
        {
            // Сеть недоступна/таймаут: считаем, что файла нет, — план строится по локальным
            // данным, а ошибка соединения всплывёт при выполнении первой операции.
            return new WebDavProbe { Exists = false };
        }
    }

    /// <summary>Скачивает файл во временный файл рядом с назначением и атомарно переносит. null — успех.</summary>
    public static async Task<string?> DownloadAsync(string fileUrl, string? user, string? password,
        string destinationPath, CancellationToken ct = default)
    {
        try
        {
            var auth = BuildAuth(user, password);
            using var request = new HttpRequestMessage(HttpMethod.Get, fileUrl);
            request.Headers.Authorization = auth;

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var temp = destinationPath + ".tmp";
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
            }
            File.Move(temp, destinationPath, overwrite: true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Загружает файл на WebDAV методом PUT (создавая родительские каталоги MKCOL). null — успех.</summary>
    public static async Task<string?> UploadAsync(string filePath, string fileUrl, string? user, string? password,
        CancellationToken ct = default)
    {
        try
        {
            var auth = BuildAuth(user, password);

            // Создаём цепочку родительских каталогов (MKCOL), 405 «уже существует» — норма.
            var parent = ParentUrl(fileUrl);
            if (!string.IsNullOrEmpty(parent))
                await EnsureFolderAsync(parent, auth, ct).ConfigureAwait(false);

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var put = new HttpRequestMessage(HttpMethod.Put, fileUrl)
            {
                Content = new StreamContent(stream)
            };
            put.Headers.Authorization = auth;
            put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var response = await Http.SendAsync(put, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Рекурсивно создаёт WebDAV-каталог (MKCOL на каждый сегмент пути).</summary>
    private static async Task EnsureFolderAsync(string folderUrl, AuthenticationHeaderValue? auth, CancellationToken ct)
    {
        if (!Uri.TryCreate(folderUrl, UriKind.Absolute, out var uri))
            return;

        var current = uri.GetLeftPart(UriPartial.Authority);
        foreach (var raw in uri.Segments.Skip(1).Select(s => Uri.UnescapeDataString(s.TrimEnd('/'))))
        {
            if (string.IsNullOrEmpty(raw))
                continue;
            current = current.TrimEnd('/') + "/" + Uri.EscapeDataString(raw);
            using var mkcol = new HttpRequestMessage(new HttpMethod("MKCOL"), current);
            mkcol.Headers.Authorization = auth;
            using var response = await Http.SendAsync(mkcol, ct).ConfigureAwait(false);
            // 201 — создано; 405/301/409/200 — уже существует или иная штатная ситуация.
        }
    }

    /// <summary>Родительский URL (до последнего слэша).</summary>
    private static string ParentUrl(string url)
    {
        var idx = url.TrimEnd('/').LastIndexOf('/');
        return idx > 0 ? url.Substring(0, idx) : string.Empty;
    }

    private static AuthenticationHeaderValue? BuildAuth(string? user, string? password)
    {
        if (string.IsNullOrEmpty(user))
            return null;
        var raw = Encoding.UTF8.GetBytes($"{user}:{password ?? string.Empty}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private static WebDavProbe Parse(HttpResponseMessage response) => new()
    {
        Exists = true,
        LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime,
        Length = response.Content.Headers.ContentLength,
        ETag = response.Headers.ETag?.Tag
    };
}