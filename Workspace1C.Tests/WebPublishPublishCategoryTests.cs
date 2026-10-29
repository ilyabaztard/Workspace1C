using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Категория логирования CM_PUBLISH (issue #63, комментарий 7OH 2026-10-28 «давайте
/// отладку и тут включать подробную»): зарегистрирована в направлениях вкладки
/// «Логирование» (WPF и Avalonia строят список из <see cref="LogCategory.DirectionCategories"/>),
/// известном списке флагов trace.json, локализована ru/en и по правилам issue #80
/// пишет INFO-записи с префиксом «[CM_PUBLISH]» только при включённых категории
/// и общем выключателе CM_ON.
/// </summary>
[Collection("TraceFlagsState")]
public sealed class WebPublishPublishCategoryTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void PublishCategory_RegisteredInDirections_AllCategories_KnownFlags()
    {
        // Категория присутствует в TraceFlags (конфиг trace.json) и в списке вкладки
        // «Логирование» (направления issue #80). По умолчанию выключена, как все.
        Assert.Equal("CM_PUBLISH", LogCategory.Publish);
        Assert.Contains(LogCategory.Publish, LogCategory.DirectionCategories);
        Assert.Contains(LogCategory.Publish, LogCategory.AllCategories);
        Assert.Contains(LogCategory.Publish, TraceFlagsFormat.KnownFlags);
        // Используется вкладками обеих платформ без правок разметки (список из кода).
        Assert.Equal("Settings.Logging.CatPublish", LogCategory.TitleKey(LogCategory.Publish));
        Assert.Equal("Settings.Logging.CatPublishDesc", LogCategory.DescriptionKey(LogCategory.Publish));
    }

    [Fact]
    public void PublishCategory_RuAndEn_Localized()
    {
        // Ключи локализации лежат в объекте «strings» (топ-уровень: code/name/strings).
        foreach (var (lang, file) in new[]
                 {
                     ("ru", Path.Combine("Workspace1C", "Localization", "Languages", "ru.json")),
                     ("en", Path.Combine("Workspace1C", "Localization", "Languages", "en.json"))
                 })
        {
            using var doc = JsonDocument.Parse(ReadProjectFile(file));
            var strings = doc.RootElement.GetProperty("strings");
            var title = strings.GetProperty("Settings.Logging.CatPublish").GetString();
            var desc = strings.GetProperty("Settings.Logging.CatPublishDesc").GetString();
            Assert.False(string.IsNullOrWhiteSpace(title), $"[{lang}] нет заголовка CM_PUBLISH");
            Assert.False(string.IsNullOrWhiteSpace(desc), $"[{lang}] нет пояснения CM_PUBLISH");
        }
    }

    [Fact]
    public void LoggingTab_BothPlatforms_BuildFromDirectionCategories()
    {
        // Вкладка «Логирование» строит списки из LogCategory.DirectionCategories —
        // новая категория появляется на ней автоматически (без отдельной разметки).
        var wpf = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Logging.cs"));
        Assert.Contains("LogCategory.DirectionCategories", wpf, StringComparison.Ordinal);
        Assert.Contains("LoggingDirectionsPanel", wpf, StringComparison.Ordinal);

        var avalonia = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.Avalonia.Logging.cs"));
        Assert.Contains("LogCategory.DirectionCategories", avalonia, StringComparison.Ordinal);

        var wpfXaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "SettingsWindow.xaml"));
        Assert.Contains("x:Name=\"LoggingDirectionsPanel\"", wpfXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LoggingDiagnosticsPanel\"", wpfXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishCategory_InfoWritten_WhenEnabled_WithMasterOn()
    {
        using var temp = new TraceIsolation();
        File.WriteAllText(Path.Combine(temp.Path, TraceFlagsFormat.ConfigFileName),
            TraceFlagsFormat.Serialize(new Dictionary<string, bool>
            {
                ["CM_ON"] = true,
                [LogCategory.Publish] = true
            }));

        // Категория включена и общий выключатель включён — INFO-записи CM_PUBLISH пишутся.
        Assert.True(TraceFlags.IsLogCategoryEnabled(LogCategory.Publish));
        Assert.True(Log.Info(LogCategory.Publish, "тестовая запись CM_PUBLISH"));
    }

    [Fact]
    public void PublishCategory_InfoNotWritten_ByDefault_OrWithoutMasterOn()
    {
        // По умолчанию всё выключено — INFO направления не пишутся.
        using (var temp = new TraceIsolation())
        {
            Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Publish));
            Assert.False(Log.Info(LogCategory.Publish, "не должно попасть в журнал"));
        }

        // Категория включена, но общий выключатель CM_ON выключен — INFO тоже не пишутся.
        using (var temp = new TraceIsolation())
        {
            File.WriteAllText(Path.Combine(temp.Path, TraceFlagsFormat.ConfigFileName),
                TraceFlagsFormat.Serialize(new Dictionary<string, bool>
                {
                    [LogCategory.Publish] = true
                }));
            Assert.False(TraceFlags.IsLogCategoryEnabled(LogCategory.Publish));
            Assert.False(Log.Info(LogCategory.Publish, "CM_ON выключен — записи нет"));
        }
    }

    /// <summary>
    /// Изолированный каталог конфига флагов на время теста (как TraceFlagsTests.TempDir):
    /// сбрасывает кэш TraceFlags при создании и восстановлении, чтобы конфиг не
    /// «протекал» в соседние тесты.
    /// </summary>
    private sealed class TraceIsolation : IDisposable
    {
        private readonly string? _previousDir;
        private readonly Func<string, string?> _previousEnv;
        private bool _disposed;

        public TraceIsolation()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"cm_publish_trace_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            _previousDir = TraceFlags.ConfigDirectoryOverride;
            _previousEnv = TraceFlags.EnvReader;
            // Сохраняем CM_REDIRECT (тесты входа портала), остальное изолируем.
            TraceFlags.EnvReader = name =>
            {
                if (string.Equals(name, TraceFlags.RedirectFlag, StringComparison.Ordinal))
                    return Environment.GetEnvironmentVariable(name);
                return null;
            };
            TraceFlags.ConfigDirectoryOverride = Path;
            TraceFlags.ResetCacheForTesting();
        }

        public string Path { get; }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            TraceFlags.ConfigDirectoryOverride = _previousDir;
            TraceFlags.EnvReader = _previousEnv;
            TraceFlags.ResetCacheForTesting();
            try { Directory.Delete(Path, recursive: true); } catch { /* не мешаем остальным тестам */ }
        }
    }
}
