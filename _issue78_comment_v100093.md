Исправлено в версии 1.0.0.93 (Windows и Linux):

1. **Заметки с секретами не сохранялись на Windows** — при сохранении заметки с секретом окно показывало «Не удалось зашифровать секрет «…»: Зашифровано, ключ недоступен» и прерывало сохранение (не сохранялся и markdown-текст карточки). Причина: `NoteCrypto.CreateDefault()` на Windows передавал `DpapiKeyProvider` как не-null провайдер, из-за чего `Encrypt()` уходил в AES-ветку, где `DpapiKeyProvider.GetKey()` возвращает null (DPAPI не использует отдельный ключ) → статус «ключ недоступен», а DPAPI-ветка шифрования была недостижима ([`NoteCrypto.cs`](Workspace1C/Services/NoteCrypto.cs)). Теперь на Windows шифрование идёт через DPAPI текущего пользователя, как задумано.
2. **Заголовок диалога ошибки** — вместо «Заметки базы: {0}» подставляется имя базы ([`BaseNotesWindow.xaml.cs`](Workspace1C/Views/BaseNotesWindow.xaml.cs), [`BaseNotesWindow.Avalonia.cs`](Workspace1C/Views/BaseNotesWindow.Avalonia.cs)).

Покрыто регрессионным тестом `NoteCrypto_CreateDefault_EncryptsAndDecrypts_Secret` ([`InfobaseNoteTests.cs`](Workspace1C.Tests/InfobaseNoteTests.cs)).

Релиз: https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.93