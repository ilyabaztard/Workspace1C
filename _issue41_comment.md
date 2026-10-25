Исправлено в версии 1.0.0.72 (Windows и Linux) по замечанию из последнего комментария: параметр создания ИБ в новом кластере теперь берётся из исходной базы автоматически.

В раб «infobase summary list» передаётся пользователь СУБД (db-user), поэтому [`ClusterMigrationService.BuildCreateRequest`](Workspace1C/Services/ClusterMigrationService.cs) использует его с приоритетом (`DbUser = FirstNonEmpty(source.DbUser, dbUser)`); значение из диалога миграции — только fallback для баз, где rac db-user не отдал. Пароль СУБД rac по-прежнему не возвращает — он приходит только из диалога (поле на форме не добавлялось, как и в 1.0.0.71).

Добавлены юнит-тесты: `ClusterMigrationServiceTests` (приоритет db-user источника, fallback на диалог, пароль всегда из формы).

Реализовано на обеих платформах.

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.72