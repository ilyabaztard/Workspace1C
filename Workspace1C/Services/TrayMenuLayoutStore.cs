using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="ITrayMenuLayoutStore"/>: компоновка меню трея (issue #62)
/// хранится в едином читаемом JSON-файле <c>tray_menu_layout.json</c> в каталоге
/// данных профиля (рядом с <c>settings.json</c>), по образцу
/// <see cref="UtilitiesMenuLayoutStore"/>. Файл можно править вручную и переносить
/// между установками. Запись — атомарная (временный файл + замена), JSON с отступами
/// и читаемой кириллицей. Битый файл не роняет загрузку: возвращается null (построитель
/// меню использует стандартную компоновку), файл на диске не трогается.
/// </summary>
public sealed class TrayMenuLayoutStore : ITrayMenuLayoutStore
{
    private readonly IProfileService? _profileService;
    private readonly string? _directoryOverride;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>Имя файла компоновки меню трея (рядом с настройками приложения).</summary>
    public const string FileName = "tray_menu_layout.json";

    /// <param name="profileService">Профиль для определения каталога данных (может быть null).</param>
    /// <param name="directoryOverride">Явный каталог хранения вместо каталога данных
    /// (используется в юнит-тестах для изоляции от реальных данных профиля).</param>
    public TrayMenuLayoutStore(IProfileService? profileService = null, string? directoryOverride = null)
    {
        _profileService = profileService;
        _directoryOverride = directoryOverride;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
            // а не \uXXXX-последовательностями (паттерн цикла 0.3.9.155).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    /// <summary>Каталог данных: явный (тесты) → каталог активного профиля → общий каталог приложения.</summary>
    private string DataDirectory =>
        !string.IsNullOrWhiteSpace(_directoryOverride)
            ? _directoryOverride!
            : (!string.IsNullOrWhiteSpace(_profileService?.CurrentProfileDataDirectory)
                ? _profileService!.CurrentProfileDataDirectory
                : PlatformPaths.AppDataDirectory);

    /// <inheritdoc />
    public string FilePath => Path.Combine(DataDirectory, FileName);

    /// <inheritdoc />
    public TrayMenuLayout? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            var json = File.ReadAllText(FilePath);
            var layout = JsonSerializer.Deserialize<TrayMenuLayout>(json, _jsonOptions);
            return TrayMenuResolver.NormalizeLayout(layout);
        }
        catch
        {
            // Битый/нечитаемый файл не должен ронять загрузку: возвращаем null —
            // построитель меню использует стандартную компоновку. Сам файл на диске
            // не трогаем (пользователь сможет его поправить вручную).
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(TrayMenuLayout layout)
    {
        if (layout is null)
            return;
        layout.SchemaVersion = 1;
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(layout, _jsonOptions);

        // Атомарная запись: сначала во временный файл, затем замена целевого
        // (как UtilitiesMenuLayoutStore.Save), чтобы не оставить битый файл при сбое.
        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
