using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты чистой логики командной палитры (Ctrl+K): фильтрация, ранжирование,
/// навигация и сброс выбора при смене запроса.
/// </summary>
public sealed class CommandPaletteViewModelTests
{
    private static CommandPaletteItem Base(string name, string subtitle = "") => new()
    {
        Kind = CommandPaletteItemKind.Base,
        Id = "id-" + name,
        Title = name,
        Subtitle = subtitle
    };

    private static CommandPaletteItem Command(string title) => new()
    {
        Kind = CommandPaletteItemKind.Command,
        Id = "cmd-" + title,
        Title = title
    };

    private static CommandPaletteViewModel CreateVm()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(
            new[] { Base("Бухгалтерия", "Файловая"), Base("Зарплата", "Сервер"), Base("Архив бухгалтерии") },
            new[] { Command("Настройки"), Command("Синхронизация с ibases.v8i") });
        return vm;
    }

    [Fact]
    public void EmptyQuery_ReturnsAll_BasesFirst()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.Equal(5, vm.VisibleItems.Count);
        Assert.Equal(CommandPaletteItemKind.Base, vm.VisibleItems[0].Kind);
        Assert.Equal(CommandPaletteItemKind.Command, vm.VisibleItems[^1].Kind);
    }

    [Fact]
    public void Filter_IsCaseInsensitive_AndMatchesSubtitle()
    {
        var vm = CreateVm();

        vm.ApplyQuery("зарп");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Зарплата", vm.VisibleItems[0].Title);

        vm.ApplyQuery("сервер");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Зарплата", vm.VisibleItems[0].Title);
    }

    [Fact]
    public void Filter_PrefixMatch_RanksAboveContains()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(
            new[] { Base("Бухгалтерия"), Base("Новая бухгалтерия") },
            System.Array.Empty<CommandPaletteItem>());

        vm.ApplyQuery("бух");
        Assert.Equal(2, vm.VisibleItems.Count);
        Assert.Equal("Бухгалтерия", vm.VisibleItems[0].Title);
    }

    [Fact]
    public void Filter_MultiWord_RequiresAllWords()
    {
        var vm = CreateVm();

        vm.ApplyQuery("архив бухгалтер");
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Архив бухгалтерии", vm.VisibleItems[0].Title);

        vm.ApplyQuery("архив зарплата");
        Assert.Empty(vm.VisibleItems);
    }

    [Fact]
    public void QueryChange_ResetsSelection()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");
        vm.MoveDown();
        vm.MoveDown();
        Assert.Equal(2, vm.SelectedIndex);

        vm.ApplyQuery("настройки");
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal("Настройки", vm.Current!.Title);
    }

    [Fact]
    public void MoveDownUp_RespectsBounds()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.True(vm.MoveDown());
        Assert.False(vm.MoveDown()); // 5 элементов, дальше нельзя
        Assert.Equal(4, vm.SelectedIndex);

        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.True(vm.MoveUp());
        Assert.False(vm.MoveUp());
        Assert.Equal(0, vm.SelectedIndex);
    }

    // ===== Issue #84: единая навигация стрелками для обеих платформ (MoveSelection) =====

    [Fact]
    public void MoveSelection_MinusOneIsUp_PlusOneIsDown()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.True(vm.MoveSelection(+1));
        Assert.Equal(1, vm.SelectedIndex);
        Assert.True(vm.MoveSelection(+1));
        Assert.Equal(2, vm.SelectedIndex);

        Assert.True(vm.MoveSelection(-1));
        Assert.Equal(1, vm.SelectedIndex);
        Assert.True(vm.MoveSelection(-1));
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_RespectsBounds_NoWrap()
    {
        // Границы НЕ зацикливаются (прежнее поведение MoveUp/MoveDown): на первом
        // элементе вверх и на последнем вниз — позиция не меняется.
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.False(vm.MoveSelection(-1));
        Assert.Equal(0, vm.SelectedIndex);

        Assert.True(vm.MoveSelection(+1));
        Assert.True(vm.MoveSelection(+1));
        Assert.True(vm.MoveSelection(+1));
        Assert.True(vm.MoveSelection(+1));
        Assert.False(vm.MoveSelection(+1));
        Assert.Equal(4, vm.SelectedIndex);
        Assert.Equal(5, vm.VisibleItems.Count);
    }

    [Fact]
    public void MoveSelection_ZeroIsNoOp()
    {
        var vm = CreateVm();
        vm.ApplyQuery("");

        Assert.False(vm.MoveSelection(0));
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_EmptyList_IsSafe()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(System.Array.Empty<CommandPaletteItem>(), System.Array.Empty<CommandPaletteItem>());

        Assert.False(vm.MoveSelection(-1));
        Assert.False(vm.MoveSelection(+1));
        Assert.Equal(-1, vm.SelectedIndex);
    }

    [Fact]
    public void Reset_ClearsQueryAndSelectsFirst()
    {
        var vm = CreateVm();
        vm.ApplyQuery("настройки");
        vm.Reset();

        Assert.Equal("", vm.Query);
        Assert.Equal(5, vm.VisibleItems.Count);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void EmptyResult_CurrentIsNull()
    {
        var vm = CreateVm();
        vm.ApplyQuery("неттакого");

        Assert.Empty(vm.VisibleItems);
        Assert.Null(vm.Current);
    }

    // ===== Пустой и null-источник (issue #312): палитра не должна падать =====

    [Fact]
    public void EmptySource_AllOperationsSafe()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(System.Array.Empty<CommandPaletteItem>(), System.Array.Empty<CommandPaletteItem>());

        vm.ApplyQuery("");
        Assert.Empty(vm.VisibleItems);
        Assert.Null(vm.Current);
        Assert.False(vm.MoveDown());
        Assert.False(vm.MoveUp());
        // Пустой список: выделения нет (SelectedIndex = -1), Current возвращает null.
        Assert.Equal(-1, vm.SelectedIndex);

        vm.ApplyQuery("бух");
        Assert.Empty(vm.VisibleItems);
        Assert.Null(vm.Current);

        vm.Reset();
        Assert.Equal("", vm.Query);
        Assert.Empty(vm.VisibleItems);
    }

    [Fact]
    public void NullSource_TreatedAsEmpty()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(null!, null!);

        Assert.Empty(vm.VisibleItems);
        vm.ApplyQuery("что-нибудь");
        Assert.Empty(vm.VisibleItems);
        Assert.Null(vm.Current);
    }

    [Fact]
    public void NullQuery_TreatedAsEmpty()
    {
        var vm = CreateVm();
        vm.ApplyQuery(null!);

        Assert.Equal(5, vm.VisibleItems.Count);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [Fact]
    public void SetSource_ReplacesPreviousContent()
    {
        var vm = CreateVm();
        Assert.Equal(5, vm.VisibleItems.Count);

        vm.SetSource(new[] { Base("Новая база") }, System.Array.Empty<CommandPaletteItem>());
        Assert.Single(vm.VisibleItems);
        Assert.Equal("Новая база", vm.VisibleItems[0].Title);
    }

    // ===== Enter/Esc-семантика на чистой логике (окно опирается на неё) =====

    [Fact]
    public void Enter_ExecutesCurrentSelection()
    {
        var vm = CreateVm();
        vm.ApplyQuery("настройки");

        // Enter в окне берёт именно Current — выбранный отфильтрованный элемент.
        Assert.Equal("Настройки", vm.Current!.Title);
        Assert.Equal(CommandPaletteItemKind.Command, vm.Current.Kind);
    }

    [Fact]
    public void Esc_ClosesWithoutSelection_AndResetRestoresState()
    {
        var vm = CreateVm();
        vm.ApplyQuery("архив");
        Assert.Single(vm.VisibleItems);

        // Reset — состояние при следующем открытии палитры: пустой запрос, полный список.
        vm.Reset();
        Assert.Equal("", vm.Query);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.Equal(5, vm.VisibleItems.Count);
    }

    [Fact]
    public void EmptySource_EnterIsNull_EscIsSafe()
    {
        var vm = new CommandPaletteViewModel();
        vm.SetSource(System.Array.Empty<CommandPaletteItem>(), System.Array.Empty<CommandPaletteItem>());

        // Enter на пустой палитре не имеет выбранного элемента (окно просто закроется),
        // Esc ничего не выбирает — оба пути безопасны.
        Assert.Null(vm.Current);
        vm.Reset();
        Assert.Empty(vm.VisibleItems);
    }
}
