# Workspace 1C 1.0.0.103

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.103

### Исправлено

- **Иконка для «Новинки программы» (issue #81, Windows и Linux)**. У «Новинки
  программы» в меню «Утилиты» теперь та же иконка, что в контекстном меню базы, —
  четырёхлучевая звезда «NEW» (materialDesign `NewBox`, #22C55E). Раньше ключ
  `IconNewBox` отсутствовал в словарях иконок (`Icons.xaml` / `Icons.axaml`), и
  меню «Утилиты» (WPF), а также оба меню на Linux (Avalonia) рисовали запасную
  «папку» вместо звезды NEW.

  Добавлены:
  - геометрия `IconNewBox` в базовые словари
    ([`Icons.xaml`](Workspace1C/Themes/Icons.xaml),
    [`Icons.axaml`](Workspace1C/Themes/Icons.axaml));
  - варианты `IconNewBox.<Набор>` во все наборы тем иконок — Material, MacOS,
    WindowsXp, Glass (`.xaml` и `.axaml`; для Glass — и полупрозрачный фоновый
    слой `IconNewBox.Back.Glass`);
  - иконки стандартных подменю (включая **NewBox**) в каталог иконок редакторов
    структуры «Утилиты» / «Контекстное меню» / «Меню в трее» —
    [`UtilitiesMenuEditorViewModel.BuildCatalogIcons()`](Workspace1C/ViewModels/UtilitiesMenuEditorViewModel.cs):
    теперь при правке структуры иконку NEW можно выбрать (issue #81: «Для выбора
    при правке иконки NEW не видно»).

### Тесты

- [`Issue81NewBoxIconTests.cs`](Workspace1C.Tests/Issue81NewBoxIconTests.cs):
  иконка «Новинки программы» одинакова в каталогах «Утилиты» и контекстного меню,
  ключ `NewBox` присутствует в каталоге иконок редактора и резолвится в геометрию
  `IconNewBox`, геометрия есть в базовых словарях и всех наборах тем обеих платформ.
  Всего в проекте **4 912 юнит-тестов**.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).