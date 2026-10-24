using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Параметры создания/правки пользовательской группы редактора меню трея
/// (issue #62): имя группы и ключ иконки каталога (пустой — без иконки).</summary>
public sealed class TrayMenuGroupEditParams
{
    public TrayMenuGroupEditParams(string title, string? iconKey)
    {
        Title = title;
        IconKey = iconKey;
    }

    /// <summary>Имя группы.</summary>
    public string Title { get; }

    /// <summary>Ключ иконки каталога (пустой — без иконки).</summary>
    public string? IconKey { get; }
}

/// <summary>
/// Редактор структуры меню трея (issue #62): дерево блоков верхнего уровня с
/// перемещением (вверх/вниз), включением/отключением (видимость), сменой стиля
/// блока («Блок меню»/«Подменю»), созданием пользовательских групп с иконками,
/// разделителями, сбросом к стандартной компоновке и сохранением/загрузкой в файл
/// <c>tray_menu_layout.json</c>. Чистый .NET — используется обеими платформами
/// (WPF и Avalonia).
/// <para>
/// Блоки «Избранные базы», «Недавние базы» и «Службы» нельзя удалить или
/// продублировать — их можно скрыть, переместить и сменить стиль (issue #62).
/// </para>
/// </summary>
public class TrayMenuEditorViewModel : ViewModelBase
{
    private readonly ITrayMenuLayoutStore? _store;
    private TrayMenuLayout _layout = TrayMenuLayouts.Default();
    private TrayMenuNodeItem? _selectedItem;
    private string _selectedIconKey = string.Empty;

    /// <param name="store">Хранилище компоновки (для загрузки/сохранения); может быть null.</param>
    public TrayMenuEditorViewModel(ITrayMenuLayoutStore? store)
    {
        _store = store;
        _layout = TrayMenuResolver.NormalizeLayout(store?.Load());
        Roots = BuildTree(_layout.Root);

        MoveUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
        MoveDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
        AddGroupCommand = new RelayCommand(p => AddGroup(p as TrayMenuGroupEditParams));
        AddSeparatorCommand = new RelayCommand(_ => AddSeparator());
        EditGroupCommand = new RelayCommand(p => EditGroup(p as TrayMenuGroupEditParams), _ => CanEditGroup);
        DeleteCommand = new RelayCommand(_ => DeleteSelected(), _ => CanDelete);
        ResetDefaultCommand = new RelayCommand(_ => ResetToDefault());
        SaveCommand = new RelayCommand(_ => Save());
        CloseCommand = new RelayCommand(_ => Accepted = true);
        ToggleVisibleCommand = new RelayCommand(_ => { /* видимость переключается чекбоксом напрямую */ });
    }

    /// <summary>Корневые узлы дерева компоновки (верхний уровень меню трея).</summary>
    public ObservableCollection<TrayMenuNodeItem> Roots { get; }

    /// <summary>Выбранный узел дерева (объект операций перемещения/стиля/иконки).</summary>
    public TrayMenuNodeItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetPropertyWithRelated(ref _selectedItem, value, nameof(SelectedItem), nameof(HasSelection),
                nameof(CanMoveSelectedUp), nameof(CanMoveSelectedDown), nameof(CanEditGroup), nameof(CanDelete),
                nameof(SelectedBlockStyle)))
            {
                // Показываем в поле выбора иконки текущую иконку узла (для групп).
                SelectedIconKey = value?.Node.IconKey ?? string.Empty;
                RaiseCommandsCanExecuteChanged();
            }
        }
    }

    /// <summary>Выбран ли узел (для UI-включения кнопок).</summary>
    public bool HasSelection => SelectedItem is not null;

    /// <summary>Можно ли поднять выбранный узел выше (для UI).</summary>
    public bool CanMoveSelectedUp => CanMoveSelected(-1);

    /// <summary>Можно ли опустить выбранный узел ниже (для UI).</summary>
    public bool CanMoveSelectedDown => CanMoveSelected(1);

    /// <summary>Можно ли править выбранный узел как пользовательскую группу (переименование/иконка).</summary>
    public bool CanEditGroup => IsUserGroup(SelectedItem);

    /// <summary>Можно ли удалить выбранный узел: только пользовательскую группу или разделитель
    /// (стандартные блоки Избранные/Недавние/Службы удалить нельзя — issue #62).</summary>
    public bool CanDelete => IsUserGroup(SelectedItem) || IsSeparator(SelectedItem);

    /// <summary>Выбранный ключ иконки каталога (поле редактора).</summary>
    public string SelectedIconKey
    {
        get => _selectedIconKey;
        set => SetProperty(ref _selectedIconKey, value);
    }

    /// <summary>
    /// Стиль выбранного блока («Блок меню»/«Подменю»), привязывается к выпадающему
    /// списку редактора. Для не-блока (разделитель) — дефолт «Блок».
    /// </summary>
    public TrayMenuBlockStyle SelectedBlockStyle
    {
        get => SelectedItem is null ? TrayMenuBlockStyle.Block : SelectedItem.EffectiveStyle;
        set
        {
            var item = SelectedItem;
            if (item is null || !string.Equals(item.Node.Kind, "block", StringComparison.Ordinal))
                return;
            item.Node.Style = value;
            item.NotifyChanged();
            OnPropertyChanged(nameof(SelectedBlockStyle));
        }
    }

    /// <summary>Доступные стили блока для выпадающего списка.</summary>
    public IReadOnlyList<TrayMenuBlockStyle> StyleOptions { get; } =
        [TrayMenuBlockStyle.Block, TrayMenuBlockStyle.Submenu];

    /// <summary>Доступные иконки каталога для пользовательских групп (ключ → подпись).</summary>
    public IReadOnlyList<UtilitiesIconOption> CatalogIcons { get; } = BuildCatalogIcons();

    /// <summary>Диалог завершён нажатием «Готово».</summary>
    public bool Accepted { get; private set; }

    /// <summary>Сохранённая компоновка (для построителя меню после закрытия редактора).</summary>
    public TrayMenuLayout Layout => _layout;

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand AddSeparatorCommand { get; }
    public RelayCommand EditGroupCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ResetDefaultCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand ToggleVisibleCommand { get; }

    /// <summary>Восстанавливает дерево верхнего уровня из компоновки.</summary>
    private static ObservableCollection<TrayMenuNodeItem> BuildTree(IEnumerable<TrayMenuNode> nodes)
    {
        var result = new ObservableCollection<TrayMenuNodeItem>();
        foreach (var node in nodes)
        {
            if (node is null)
                continue;
            var item = new TrayMenuNodeItem(node, null);
            result.Add(item);
        }
        return result;
    }

    private bool CanMoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return false;
        var index = Roots.IndexOf(item);
        var target = index + delta;
        return target >= 0 && target < Roots.Count;
    }

    private void MoveSelected(int delta)
    {
        var item = SelectedItem;
        if (item is null)
            return;
        var index = Roots.IndexOf(item);
        var target = index + delta;
        if (target < 0 || target >= Roots.Count)
            return;
        Roots.Move(index, target);
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Добавляет узел-разделитель после выбранного узла (или в конец корня) — issue #62.</summary>
    private void AddSeparator()
    {
        var node = TrayMenuLayouts.Separator();
        var selected = SelectedItem;
        if (selected is not null)
        {
            var index = Roots.IndexOf(selected);
            var item = new TrayMenuNodeItem(node, null);
            Roots.Insert(index + 1, item);
            SelectedItem = item;
        }
        else
        {
            var item = new TrayMenuNodeItem(node, null);
            Roots.Add(item);
            SelectedItem = item;
        }
    }

    private void AddGroup(TrayMenuGroupEditParams? editParams)
    {
        var title = editParams is not null && !string.IsNullOrWhiteSpace(editParams.Title)
            ? editParams.Title!.Trim()
            : LocalizationManager.T("TrayMenu.NewGroup");
        var node = new TrayMenuNode
        {
            Kind = "block",
            BlockKind = TrayMenuBlockKind.UserGroup,
            GroupId = "ug-" + Guid.NewGuid().ToString("N")[..8],
            CustomTitle = title,
            Style = TrayMenuLayouts.DefaultStyle(TrayMenuBlockKind.UserGroup),
        };
        ApplyIconToNode(node, editParams?.IconKey);
        var item = new TrayMenuNodeItem(node, null);
        Roots.Add(item);
        SelectedItem = item;
    }

    /// <summary>Переименование/иконка выбранной пользовательской группы (диалог «Правка группы»).</summary>
    private void EditGroup(TrayMenuGroupEditParams? editParams)
    {
        var item = SelectedItem;
        if (item is null || !IsUserGroup(item) || editParams is null)
            return;
        if (!string.IsNullOrWhiteSpace(editParams.Title))
            item.Node.CustomTitle = editParams.Title!.Trim();
        ApplyIconToNode(item.Node, editParams.IconKey);
        item.NotifyChanged();
    }

    /// <summary>Удаление выбранного узла: разделитель или пользовательская группа. Стандартные
    /// блоки (Избранные/Недавние/Службы) защищены от удаления (issue #62).</summary>
    private void DeleteSelected()
    {
        var item = SelectedItem;
        if (item is null || !CanDelete)
            return;
        var index = Roots.IndexOf(item);
        Roots.Remove(item);
        SelectedItem = null;
        SelectNodeAt(index);
        RaiseCommandsCanExecuteChanged();
    }

    /// <summary>Выбирает узел на месте удалённого: тот же индекс в списке, если индекс за концом — последний.</summary>
    private void SelectNodeAt(int index)
    {
        if (Roots.Count == 0)
        {
            SelectedItem = null;
            return;
        }
        SelectedItem = Roots[Math.Min(index, Roots.Count - 1)];
    }

    /// <summary>Назначает иконку каталога узлу (ключ → ключ+цвет, сброс внешнего файла).</summary>
    private void ApplyIconToNode(TrayMenuNode node, string? iconKey)
    {
        if (node is null || string.IsNullOrWhiteSpace(iconKey))
            return;
        var option = CatalogIcons.FirstOrDefault(o => string.Equals(o.Key, iconKey, StringComparison.Ordinal));
        if (option is null)
            return;
        node.IconKey = option.Key;
        node.IconColor = option.Color;
        node.ExternalIconPath = null;
    }

    private static bool IsUserGroup(TrayMenuNodeItem? item) =>
        item is not null &&
        string.Equals(item.Node.Kind, "block", StringComparison.Ordinal) &&
        item.Node.BlockKind == TrayMenuBlockKind.UserGroup;

    private static bool IsSeparator(TrayMenuNodeItem? item) =>
        item is not null && string.Equals(item.Node.Kind, "separator", StringComparison.Ordinal);

    private void ResetToDefault()
    {
        _layout = TrayMenuLayouts.Default();
        Roots.Clear();
        foreach (var item in BuildTree(_layout.Root))
            Roots.Add(item);
        SelectedItem = null;
    }

    private void Save()
    {
        var layout = new TrayMenuLayout { SchemaVersion = 1 };
        foreach (var item in Roots)
            layout.Root.Add(ToNode(item));
        _layout = layout;
        try
        {
            _store?.Save(layout);
        }
        catch
        {
            // Не удалось сохранить — редактор остаётся открытым; ошибку покажет вызывающий код.
        }
    }

    private static TrayMenuNode ToNode(TrayMenuNodeItem item)
    {
        var node = new TrayMenuNode
        {
            Kind = item.Node.Kind,
            BlockKind = item.Node.BlockKind,
            GroupId = item.Node.GroupId,
            CustomTitle = item.Node.CustomTitle,
            IconKey = item.Node.IconKey,
            IconColor = item.Node.IconColor,
            ExternalIconPath = item.Node.ExternalIconPath,
            Visible = item.Node.Visible,
            Style = item.Node.Style,
            SeparatorBefore = item.Node.SeparatorBefore,
        };
        foreach (var child in item.Children)
            node.Children.Add(ToNode(child));
        return node;
    }

    private void RaiseCommandsCanExecuteChanged()
    {
        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        EditGroupCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Каталог иконок для пользовательских групп меню трея: небольшой набор
    /// ключ+цвет («простой набор цвета/ключа» по issue #62; механизм иконок
    /// <see cref="UtilitiesMenuEditorViewModel"/> переиспользуется).</summary>
    private static IReadOnlyList<UtilitiesIconOption> BuildCatalogIcons() =>
    [
        new UtilitiesIconOption("Star", "#F59E0B"),
        new UtilitiesIconOption("Folder", "#3B82F6"),
        new UtilitiesIconOption("FolderStar", "#8B5CF6"),
        new UtilitiesIconOption("Database", "#10B981"),
        new UtilitiesIconOption("Server", "#EF4444"),
    ];
}

/// <summary>
/// Узел дерева редактора меню трея: обёртка над <see cref="TrayMenuNode"/> с
/// родителем (для операций перемещения). Изменения свойств узла транслируются
/// в <see cref="TrayMenuNode"/> компоновки.
/// </summary>
public sealed class TrayMenuNodeItem : ViewModelBase
{
    /// <param name="node">Узел компоновки, который оборачиваем.</param>
    /// <param name="parent">Родительский узел дерева (null — корень).</param>
    public TrayMenuNodeItem(TrayMenuNode node, TrayMenuNodeItem? parent)
    {
        Node = node;
        Parent = parent;
    }

    /// <summary>Оборачиваемый узел компоновки.</summary>
    public TrayMenuNode Node { get; }

    /// <summary>Родительский узел дерева (null — корневой список).</summary>
    public TrayMenuNodeItem? Parent { get; set; }

    /// <summary>Дочерние узлы (зарезервировано — для двухуровневого дерева).</summary>
    public ObservableCollection<TrayMenuNodeItem> Children { get; } = new();

    /// <summary>Заголовок узла: разделитель / локализованное имя блока / пользовательский
    /// заголовок группы.</summary>
    public string Title
    {
        get
        {
            if (string.Equals(Node.Kind, "separator", StringComparison.Ordinal))
                return LocalizationManager.T("TrayMenu.Separator");
            if (!string.Equals(Node.Kind, "block", StringComparison.Ordinal))
                return Node.Kind ?? "?";
            return Node.BlockKind switch
            {
                TrayMenuBlockKind.Favorites => LocalizationManager.T("TrayMenu.BlockFavorites"),
                TrayMenuBlockKind.Recent => LocalizationManager.T("TrayMenu.BlockRecent"),
                TrayMenuBlockKind.Services => LocalizationManager.T("TrayMenu.BlockServices"),
                _ => !string.IsNullOrWhiteSpace(Node.CustomTitle)
                    ? Node.CustomTitle!
                    : LocalizationManager.T("TrayMenu.NewGroup"),
            };
        }
    }

    /// <summary>Пометка вида: «подменю»/«блок»/«разделитель» для списка.</summary>
    public string KindLabel => IsSeparator
        ? LocalizationManager.T("TrayMenu.Separator")
        : Node.BlockKind == TrayMenuBlockKind.UserGroup
            ? LocalizationManager.T("TrayMenu.BlockUserGroup")
            : Node.BlockKind switch
            {
                TrayMenuBlockKind.Favorites => LocalizationManager.T("TrayMenu.BlockFavorites"),
                TrayMenuBlockKind.Recent => LocalizationManager.T("TrayMenu.BlockRecent"),
                TrayMenuBlockKind.Services => LocalizationManager.T("TrayMenu.BlockServices"),
                _ => LocalizationManager.T("TrayMenu.BlockUserGroup"),
            };

    /// <summary>True — узел является разделителем.</summary>
    public bool IsSeparator => string.Equals(Node.Kind, "separator", StringComparison.Ordinal);

    /// <summary>Видимость узла (чекбокс в дереве).</summary>
    public bool IsVisible
    {
        get => Node.Visible;
        set
        {
            Node.Visible = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Эффективный стиль блока (дефолт по виду блока, если явный не задан).</summary>
    public TrayMenuBlockStyle EffectiveStyle => Node.Style ?? TrayMenuLayouts.DefaultStyle(Node.BlockKind);

    /// <summary>Ключ иконки для отображения в дереве (пусто — без иконки).</summary>
    public string DisplayIconKey =>
        IsSeparator ? "Minus" : (Node.IconKey ?? string.Empty);

    /// <summary>Цвет иконки (#RRGGBB) для отображения в дереве.</summary>
    public string DisplayIconColor => IsSeparator ? "#9CA3AF" : (Node.IconColor ?? "#9CA3AF");

    /// <summary>Есть ли иконка для отображения в дереве (видимость превью).</summary>
    public bool HasDisplayIcon => !string.IsNullOrWhiteSpace(DisplayIconKey);

    /// <summary>Оповещает UI об изменении внешних свойств узла (заголовок, иконка, стиль).</summary>
    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(DisplayIconKey));
        OnPropertyChanged(nameof(DisplayIconColor));
        OnPropertyChanged(nameof(HasDisplayIcon));
    }
}
