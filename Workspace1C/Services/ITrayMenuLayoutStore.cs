using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище компоновки меню трея (issue #62): единый читаемый JSON-файл
/// <c>tray_menu_layout.json</c> в каталоге данных профиля (рядом с
/// <c>settings.json</c>), по образцу <see cref="IUtilitiesMenuLayoutStore"/>. Файл
/// можно править вручную и переносить между установками. Запись — атомарная
/// (временный файл + замена), JSON с отступами и читаемой кириллицей.
/// </summary>
public interface ITrayMenuLayoutStore
{
    /// <summary>Путь к файлу компоновки (каталог данных профиля / явный override для тестов).</summary>
    string FilePath { get; }

    /// <summary>
    /// Загружает компоновку меню трея. Если файла нет — возвращает null (построитель
    /// меню использует стандартную компоновку). Битый/несовместимый файл не роняет
    /// загрузку: возвращается null, файл на диске не трогается (пользователь сможет
    /// его поправить вручную).
    /// </summary>
    TrayMenuLayout? Load();

    /// <summary>Сохраняет компоновку меню трея (атомарная запись).</summary>
    void Save(TrayMenuLayout layout);
}
