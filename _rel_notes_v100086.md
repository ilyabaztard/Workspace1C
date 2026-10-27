# Workspace 1C 1.0.0.86

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.86

### Добавлено

- **Анализ технологического журнала (окно «Технологический журнал» → кнопка
  «Анализировать…», Windows и Linux)** — разбор уже собранных логов из каталога
  журнала по шаблонам logcfg.xml (п.4). Окно анализа показывает:
  - **топы долгих операций**: вызовы серверных методов (`CALL`), запросы к
    СУБД (`DBMSSQL`, `DBPOSTGRS`, `DBORACLE` и др.) и запросы языка запросов
    (`SDBL`) — группа по тексту запроса/вызова: количество, максимальная,
    средняя и суммарная длительность в миллисекундах (длительность журнала
    пересчитывается из микросекунд платформы);
  - **сводку блокировок**: ожидания управляемых блокировок (`TLOCK`) и
    взаимоблокировки (`TDEADLOCK`) с группировкой по объектам (`Objects`) —
    количество, суммарное и максимальное время ожидания;
  - **исключения `EXCP`**, сгруппированные по нормализованному контексту:
    из описания убираются длительности, времена, счётчики и имена временных
    таблиц — одинаковые ошибки с разными динамическими данными схлопываются
    в одну группу;
  - **отбор по периоду**, **по базе** (контекст/процесс) и **по пользователю**
    (`usr`); порог длительности переносится из окна ТЖ;
  - **экспорт в CSV** (секции топов, блокировок и исключений, UTF-8 с BOM).
  ([`TechLogReader.cs`](Workspace1C/Services/TechLogParser/TechLogReader.cs),
  [`TechLogAnalyzer.cs`](Workspace1C/Services/TechLogParser/TechLogAnalyzer.cs),
  [`TechLogAnalysisViewModel.cs`](Workspace1C/ViewModels/TechLogAnalysisViewModel.cs),
  [`TechLogAnalysisWindow.cs`](Workspace1C/Views/TechLogAnalysisWindow.cs),
  [`TechLogAnalysisWindow.Avalonia.cs`](Workspace1C/Views/TechLogAnalysisWindow.Avalonia.cs),
  [`TechLogWindow.cs`](Workspace1C/Views/TechLogWindow.cs),
  [`TechLogWindow.Avalonia.cs`](Workspace1C/Views/TechLogWindow.Avalonia.cs)).

Покрыто тестами `TechLogParserTests` (+15): разбор строк ТЖ (в т.ч. минимальные
строки без длительности/пользователя, многострочные контексты и строки без
префикса даты), определение базы из `p:processName`, фильтры по базе,
пользователю и периоду, топы по суммарной длительности с группировкой,
порог длительности только для «долгих» операций, группировка исключений по
нормализованному контексту, сводка блокировок по объектам, CSV-экспорт.

Подробности — в [CHANGELOG.md](https://github.com/sivatorov/Workspace1C/blob/main/CHANGELOG.md).