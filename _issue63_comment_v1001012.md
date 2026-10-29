Исправлено в версии **1.0.1.2** (Windows и Linux):

В окне «Публикация на веб-сервере» файл алиасов (например, `1c8Aliases27.conf`)
мог НЕ появляться в списке webinst, когда детектор инстансов Apache не находил
службу/конфиг (нестандартная служба, недоступный реестр) — fallback-ветка
очищала список файлов. Теперь при ручном указании/правке поля конфигурации
строится «синтетический» инстанс из конфига на диске
([`WebPublishService.BuildInstanceFromConf`](Workspace1C/Services/WebPublishService.cs))
через тот же include-граф/Define (`ApacheInstanceDetector.PopulateIncludes`), и в
списке появляются главный конфиг и файл алиасов, а каталог публикации и URL с
портом берутся из конфига
([`WebPublishViewModel`](Workspace1C/ViewModels/WebPublishViewModel.cs)).

Дополнительно добавлено направление отладки **CM_PUBLISH** (выключено по
умолчанию, работает под общим выключателем CM_ON из issue #80): пишет найденные
инстансы (служба/конфиг/ServerRoot), причину fallback, include-граф, Define,
найденные alias-файлы и попадание их в список. Категория автоматически появилась
на вкладке «Логирование» (WPF и Avalonia) с пояснением ru/en
([`LogCategory`](Workspace1C/Services/LogCategory.cs),
[`TraceFlagsFormat`](Workspace1C/Services/TraceFlagsFormat.cs)).

Покрыто тестами
[`WebPublishIssue63Tests.cs`](Workspace1C.Tests/WebPublishIssue63Tests.cs)
(+fallback-сценарий на фикстурах 27_conf: в ConfTargets ровно
`1c8Aliases27.conf`, каталог `…\htdocs\27\demo_27`, URL
`http://localhost:8027/demo_27/`) и новым
[`WebPublishPublishCategoryTests.cs`](Workspace1C.Tests/WebPublishPublishCategoryTests.cs)
(CM_PUBLISH).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.2
