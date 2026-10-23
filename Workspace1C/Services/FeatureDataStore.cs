using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace1C.Services;

/// <summary>
/// Простое JSON-хранилище настроек новых функций (выпуск 1.0.0.69): файл лежит
/// в каталоге данных текущего профиля (как <c>custom_actions.json</c>), читается
/// и пишется целиком. Битый файл не роняет функцию — возвращается значение
/// по умолчанию, файл на диске не перезаписывается до явного сохранения.
/// </summary>
public static class FeatureDataStore
{
    /// <summary>Переопределение каталога (тесты).</summary>
    public static string? DirectoryOverride { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Каталог данных текущего профиля.</summary>
    public static string DataDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DirectoryOverride))
                return DirectoryOverride!;
            try
            {
                var profiles = AppServices.TryGetService<IProfileService>();
                var dir = profiles?.CurrentProfileDataDirectory;
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir!;
            }
            catch
            {
                // DI ещё не настроен (CLI, тесты) — каталог приложения.
            }
            return PlatformPaths.AppDataDirectory;
        }
    }

    public static string PathOf(string fileName) => Path.Combine(DataDirectory, fileName);

    public static T Load<T>(string fileName) where T : new()
    {
        var path = PathOf(fileName);
        try
        {
            if (!File.Exists(path))
                return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public static void Save<T>(string fileName, T value)
    {
        var path = PathOf(fileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
