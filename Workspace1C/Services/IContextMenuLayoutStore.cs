using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище компоновки контекстного меню базы (issue #56): единый читаемый
/// JSON-файл <c>context_menu_layout.json</c> в каталоге данных профиля (рядом
/// с <c>settings.json</c>), по образцу <see cref="IUtilitiesMenuLayoutStore"/>.
/// Файл можно править вручную и переносить между установками. Запись — атомарная
/// (временный файл + замена), JSON с отступами и читаемой кириллицей.
/// </summary>
public interface IContextMenuLayoutStore
{
    /// <summary>Путь к файлу компоновки (каталог данных профиля / явный override для тестов).</summary>
    string FilePath { get; }

    /// <summary>
    /// Загружает компоновку меню. Если файла нет — возвращает null (построитель меню
    /// использует стандартную компоновку). Битый/несовместимый файл не роняет загрузку:
    /// возвращается null, файл на диске не трогается (пользователь сможет его поправить).
    /// </summary>
    ContextMenuLayout? Load();

    /// <summary>Сохраняет компоновку меню (атомарная запись).</summary>
    void Save(ContextMenuLayout layout);
}