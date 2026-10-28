Исправлено в версии **1.0.1.0** (Windows и Linux) по комментарию 7OH от 2026-10-28 17:23
(иконка NEW в реальном меню «Утилиты» всё ещё не та):

- **Причина.** После 1.0.0.103 каталог и редактор структуры уже отдавали для группы
  «Новинки программы» звезду NEW (`NewBox`, #22C55E) — но сохранённые ранее
  layout-файлы (`utilities_menu_layout.json`, записанные до 1.0.0.103, когда ключа
  геометрии `IconNewBox` ещё не было) могли записать в группу запасную «папку» или
  пустую иконку. Реальное меню «Утилиты» показывало эту легаси-иконку, хотя каталог
  и контекстное меню уже рисовали NEW.

- **Исправление.** Легаси-значения распознаются
  ([`UtilitiesMenuCatalog.IsLegacyFallbackGroupIcon()`](Workspace1C/Services/UtilitiesMenuCatalog.cs):
  пустая иконка, запасная `IconFolder`, имя `Folder`) и сбрасываются к стандартной
  иконке каталога `NewBox` через
  [`UtilitiesMenuResolver.FixLegacyNewItemsIcon()`](Workspace1C/Services/UtilitiesMenuResolver.cs).
  Дополнительно расставлены хард-гарды в построителях меню — WPF
  ([`MainWindow.UtilitiesMenu.cs`](Workspace1C/Views/MainWindow.UtilitiesMenu.cs)) и
  Avalonia ([`MainWindow.Avalonia.Tree.cs`](Workspace1C/Views/MainWindow.Avalonia.Tree.cs)).
  Теперь иконка «Новинки программы» в реальном меню «Утилиты», в контекстном меню и
  в редакторе структуры одинакова — звезда NEW.

- **Пользовательская иконка,** назначенная в редакторе структуры, сохраняется:
  сбрасываются только легаси-фолбэки «папки».

Покрыто тестами-стражами
[`Issue81NewBoxIconTests.cs`](Workspace1C.Tests/Issue81NewBoxIconTests.cs) (+2:
распознавание легаси-иконки и сброс к `NewBox`, хард-гарды в построителях меню).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.0