Исправлено в версии 1.0.0.72 (Windows и Linux) по замечанию: «Создал группу, но переместить туда 2 блока не смог».

В редакторе структуры меню трея добавлены кнопки:
- **«В группу…»** — диалог выбора пользовательской группы (подменю); выбранный блок или разделитель переносится в её вложенное меню ([`TrayMenuGroupPickWindow.xaml`](Workspace1C/Views/TrayMenuGroupPickWindow.xaml) + [`TrayMenuGroupPickWindow.Avalonia.cs`](Workspace1C/Views/TrayMenuGroupPickWindow.Avalonia.cs));
- **«В корень»** — возврат блока из группы в корень (сразу после группы).

Дерево редактора теперь двухуровневое (блоки внутри групп), операции «Вверх/Вниз/Удалить» работают на уровне текущей группы; резолвер [`TrayMenuResolver`](Workspace1C/Services/TrayMenuResolver.cs) учитывает дочерние узлы и отбрасывает скрытые, построитель [`TrayMenuBuilder`](Workspace1C/Services/TrayMenuBuilder.cs) строит группы как подменю с содержимым (адаптеры WPF/Avalonia уже умеют `Children`). Старые файлы `tray_menu_layout.json` (без вложенности) читаются нормализацией без потерь.

Локализация ru/en; добавлены юнит-тесты `TrayMenuGroupMoveTests` (перенос в группу/в корень, разделители, запрет вложения групп, фильтр скрытых детей, построение подменю).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.72