# Workspace 1C 1.0.0.93

Релиз доступен для **Windows** (WPF) и **Linux** (Avalonia).
Скачать: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.93

### Исправлено

- **Заметки к базе не сохранялись на Windows (issue #78)** — при сохранении заметки
  с секретом окно показывало «Не удалось зашифровать секрет «…»: Зашифровано, ключ
  недоступен» и прерывало сохранение (не сохранялся и markdown-текст карточки).
  Причина: [`NoteCrypto.CreateDefault()`](Workspace1C/Services/NoteCrypto.cs) на
  Windows передавал `DpapiKeyProvider` как не-null провайдер, из-за чего
  [`NoteCrypto.Encrypt()`](Workspace1C/Services/NoteCrypto.cs) уходил в AES-ветку,
  где `DpapiKeyProvider.GetKey()` возвращает null (DPAPI не использует отдельный
  ключ) → статус «ключ недоступен», а DPAPI-ветка шифрования была недостижима.
  Теперь на Windows `CreateDefault()` не передаёт провайдер (null) — шифрование
  идёт через DPAPI текущего пользователя, как задумано. Исправление общее для
  WPF ([`BaseNotesWindow.xaml.cs`](Workspace1C/Views/BaseNotesWindow.xaml.cs)) и
  Avalonia ([`BaseNotesWindow.Avalonia.cs`](Workspace1C/Views/BaseNotesWindow.Avalonia.cs)).
  Заодно исправлен заголовок диалога ошибки: вместо «Заметки базы: {0}» теперь
  подставляется имя базы.

### Тесты

- [`InfobaseNoteTests.cs`](Workspace1C.Tests/InfobaseNoteTests.cs): регрессия
  `NoteCrypto_CreateDefault_EncryptsAndDecrypts_Secret` — платформенный экземпляр
  `NoteCrypto.CreateDefault()` шифрует и расшифровывает секрет без ошибок
  (Windows: DPAPI; Linux: ключовой файл профиля). Всего **4850** юнит-тестов
  (все проходят на Windows; Linux — 0 ошибок).