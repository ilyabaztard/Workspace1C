using Workspace1C.Models;

namespace Workspace1C.Services.Sync;

/// <summary>
/// Хранилище настроек синхронизации (п.9, 1.0.0.91): файл <c>settings_sync.json</c>
/// в каталоге данных профиля через <see cref="FeatureDataStore"/> (как NoteStore и другие
/// JSON-хранилища функций). Битый файл возвращает настройки по умолчанию — функция
/// не падает (конвенция FeatureDataStore).
/// </summary>
public static class SettingsSyncStore
{
    /// <summary>Загружает настройки (пусто — новые по умолчанию).</summary>
    public static SettingsSyncSettings Load() =>
        FeatureDataStore.Load<SettingsSyncSettings>(SettingsSyncSettings.FileName);

    /// <summary>Сохраняет настройки.</summary>
    public static void Save(SettingsSyncSettings settings) =>
        FeatureDataStore.Save(SettingsSyncSettings.FileName, settings);
}