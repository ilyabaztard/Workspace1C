using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты п.8 «Заметки к базе» (1.0.0.90): round-trip шифрования секретов (AES-GCM),
/// состояние «ключ недоступен», хранилище <see cref="NoteStore"/> (get/save/delete/search/
/// purge/import), включение заметок в поиск палитры команд
/// (<see cref="CommandPaletteViewModel"/>), секция «Заметки» в HTML-отчёте
/// (<see cref="HtmlReportExporter.MarkdownToHtml"/>/BuildDocument) и перенос заметок
/// через JSON-снимок (только markdown + имена секретов, без значений).
/// </summary>
public class InfobaseNoteTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "w1c_notes_tests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Временная папка теста — чистка без фейла при блокировке файлов.
        }
    }

    // ---------- Шифрование: чистые функции AES-GCM ----------

    [Fact]
    public void AesGcm_RoundTrip_ReturnsOriginal()
    {
        var key = AesGcmBox.GenerateKey();
        const string secret = "пароль-к-1С-Фреш-⚡";

        var cipher = AesGcmBox.Encrypt(secret, key);
        Assert.NotNull(cipher);
        Assert.NotEqual(secret, cipher);
        // Шифртекст не содержит открытого значения (маскировка в хранилище).
        Assert.DoesNotContain("пароль", cipher);

        var plain = AesGcmBox.Decrypt(cipher!, key);
        Assert.Equal(secret, plain);
    }

    [Fact]
    public void AesGcm_WrongKey_ReturnsNull()
    {
        var key = AesGcmBox.GenerateKey();
        var wrong = AesGcmBox.GenerateKey();
        var cipher = AesGcmBox.Encrypt("top secret", key);

        var plain = AesGcmBox.Decrypt(cipher!, wrong);
        Assert.Null(plain);
        Assert.DoesNotContain("top secret", cipher!);
    }

    [Fact]
    public void AesGcm_EmptyAndGarbage_Safe()
    {
        var key = AesGcmBox.GenerateKey();
        Assert.Equal(string.Empty, AesGcmBox.Encrypt(string.Empty, key));
        Assert.Equal(string.Empty, AesGcmBox.Decrypt(string.Empty, key));
        Assert.Null(AesGcmBox.Decrypt("!!!not-base64!!!", key));
        Assert.Null(AesGcmBox.Decrypt("aGVsbG8=", key)); // слишком короткие данные
        Assert.Null(AesGcmBox.Decrypt("x", new byte[16])); // неверная длина ключа
    }

    // ---------- Шифрование: NoteCrypto с инжектированным ключом ----------

    private sealed class FixedKeyProvider(byte[] key) : INoteCryptoKeyProvider
    {
        public byte[]? GetKey() => key;
        public string Describe() => "test-key";
    }

    [Fact]
    public void NoteCrypto_EncryptDecrypt_RoundTrip_WithInjectedKey()
    {
        var key = AesGcmBox.GenerateKey();
        var crypto = new NoteCrypto(new FixedKeyProvider(key));

        var encrypted = crypto.Encrypt("пароль123");
        Assert.Equal(NoteCryptoStatus.Ok, encrypted.Status);
        Assert.StartsWith(NoteCrypto.PrefixAesProfile, encrypted.Value);
        Assert.DoesNotContain("пароль123", encrypted.Value);

        var decrypted = crypto.Decrypt(encrypted.Value);
        Assert.Equal(NoteCryptoStatus.Ok, decrypted.Status);
        Assert.Equal("пароль123", decrypted.Value);
    }

    [Fact]
    public void NoteCrypto_WrongInjectedKey_KeyUnavailableWithoutException()
    {
        var crypto = new NoteCrypto(new FixedKeyProvider(AesGcmBox.GenerateKey()));
        var encrypted = crypto.Encrypt("секрет");
        Assert.Equal(NoteCryptoStatus.Ok, encrypted.Status);

        var other = new NoteCrypto(new FixedKeyProvider(AesGcmBox.GenerateKey()));
        var decrypted = other.Decrypt(encrypted.Value);
        Assert.Equal(NoteCryptoStatus.KeyUnavailable, decrypted.Status);
        Assert.Null(decrypted.Value);
    }

    [Fact]
    public void NoteCrypto_MissingKey_KeyUnavailableWithoutException()
    {
        var noKey = new NoteCrypto(new FixedKeyProvider(new byte[0]));
        var encrypted = noKey.Encrypt("x");
        Assert.Equal(NoteCryptoStatus.KeyUnavailable, encrypted.Status);

        var empty = new NoteCrypto(new FixedKeyProvider(AesGcmBox.GenerateKey()));
        Assert.Equal(NoteCryptoStatus.Ok, empty.Encrypt(null).Status);
        Assert.Equal(string.Empty, empty.Encrypt(string.Empty).Value);
        Assert.Equal(string.Empty, empty.Decrypt(string.Empty).Value);
    }

    [Fact]
    public void NoteCrypto_UnknownPrefix_NonPortable()
    {
        var crypto = new NoteCrypto(new FixedKeyProvider(AesGcmBox.GenerateKey()));
        var result = crypto.Decrypt("unknown-format-value");
        Assert.Equal(NoteCryptoStatus.NonPortable, result.Status);
    }

    [Fact]
    public void NoteCrypto_StatusTextKeys_AreStable()
    {
        Assert.Equal("Notes.SecretLocked", NoteCrypto.StatusTextKey(NoteCryptoStatus.KeyUnavailable));
        Assert.Equal("Notes.SecretNonPortable", NoteCrypto.StatusTextKey(NoteCryptoStatus.NonPortable));
        Assert.Equal(string.Empty, NoteCrypto.StatusTextKey(NoteCryptoStatus.Ok));
    }

    // ---------- NoteStore: get/save/delete/search/purge/import ----------

    private InfobaseNote MakeNote(string id, string markdown, params string[] secretNames)
    {
        var secrets = secretNames.ToDictionary(n => n, _ => "v2:encrypted");
        return new InfobaseNote { InfobaseId = id, Markdown = markdown, Secrets = secrets };
    }

    [Fact]
    public void NoteStore_SaveGetDelete_RoundTrip()
    {
        var store = new NoteStore(_tempDir);
        var note = MakeNote("base-1", "## Для кого\nАдминистраторы", "пароль");
        store.Save(note);

        var loaded = store.Get("base-1");
        Assert.NotNull(loaded);
        Assert.Equal("base-1", loaded!.InfobaseId);
        Assert.Contains("Администраторы", loaded.Markdown);
        Assert.True(loaded.Secrets.ContainsKey("пароль"));
        Assert.EndsWith(".json", Path.GetFileName(store.FilePath));

        Assert.True(store.Delete("base-1"));
        Assert.Null(store.Get("base-1"));
        Assert.False(store.Delete("base-1"));
    }

    [Fact]
    public void NoteStore_SaveEmptyNote_RemovesEntry()
    {
        var store = new NoteStore(_tempDir);
        store.Save(MakeNote("base-1", "текст"));
        Assert.NotNull(store.Get("base-1"));

        store.Save(new InfobaseNote { InfobaseId = "base-1", Markdown = "   ", Secrets = new() });
        Assert.Null(store.Get("base-1"));
    }

    [Fact]
    public void NoteStore_Search_MatchesMarkdownAndSecretNames()
    {
        var store = new NoteStore(_tempDir);
        store.Save(MakeNote("a", "Для бухгалтерии, контакт: admin@corp.ru"));
        store.Save(MakeNote("b", "Для склада", "webdav-пароль"));
        store.Save(MakeNote("c", "Для ИТ"));

        var byWord = NoteStore.SearchNotes(store.GetAll(), "бухгалтерии");
        Assert.Single(byWord);
        Assert.Equal("a", byWord[0].InfobaseId);

        var bySecretName = NoteStore.SearchNotes(store.GetAll(), "webdav");
        Assert.Single(bySecretName);
        Assert.Equal("b", bySecretName[0].InfobaseId);

        // Зашифрованные значения в поиске не участвуют.
        var byCipher = NoteStore.SearchNotes(store.GetAll(), "encrypted");
        Assert.Empty(byCipher);

        Assert.Empty(NoteStore.SearchNotes(store.GetAll(), "несуществующее"));
    }

    [Fact]
    public void NoteStore_Purge_RemovesOnlyMissingKeys()
    {
        var store = new NoteStore(_tempDir);
        store.Save(MakeNote("a", "A"));
        store.Save(MakeNote("b", "B"));

        var removed = store.PurgeMissing(new[] { "a" });
        Assert.Equal(1, removed);
        Assert.NotNull(store.Get("a"));
        Assert.Null(store.Get("b"));
    }

    [Fact]
    public void NoteStore_ImportTransferred_KeepsLocalNotePriority()
    {
        var store = new NoteStore(_tempDir);
        store.Save(MakeNote("a", "локальная заметка"));

        var transferred = new[]
        {
            new InfobaseNoteTransfer { InfobaseId = "a", Markdown = "переносимая (должна быть проигнорирована)" },
            new InfobaseNoteTransfer { InfobaseId = "b", Markdown = "новая карточка", SecretNames = { "пароль" } },
            new InfobaseNoteTransfer { InfobaseId = "c", Markdown = "   " },
            new InfobaseNoteTransfer { InfobaseId = "" }
        };

        var imported = store.ImportTransferred(transferred);
        Assert.Equal(1, imported);

        var a = store.Get("a");
        Assert.Contains("локальная", a!.Markdown);

        var b = store.Get("b");
        Assert.Equal("новая карточка", b!.Markdown);

        Assert.Null(store.Get("c"));
    }

    // ---------- Поиск: заметка участвует в фильтре палитры команд ----------

    [Fact]
    public void CommandPalette_Filter_MatchesNoteSearchText()
    {
        var items = new[]
        {
            new CommandPaletteItem
            {
                Kind = CommandPaletteItemKind.Base,
                Id = "b1",
                Title = "Бухгалтерия",
                Subtitle = "Группа",
                SearchText = "контакт: admin@corp.ru, обновление по четвергам"
            },
            new CommandPaletteItem
            {
                Kind = CommandPaletteItemKind.Base,
                Id = "b2",
                Title = "Склад"
            }
        };

        var matched = CommandPaletteViewModel.Filter(items.ToList(), "admin@corp.ru");
        Assert.Single(matched);
        Assert.Equal("b1", matched[0].Id);

        // Слова запроса должны найтись все.
        var multi = CommandPaletteViewModel.Filter(items.ToList(), "admin@corp.ru четвергам");
        Assert.Single(multi);

        Assert.Empty(CommandPaletteViewModel.Filter(items.ToList(), "никакого-такого-слова"));
    }

    // ---------- HTML-отчёт: конвертация markdown и секция «Заметки» ----------

    [Fact]
    public void MarkdownToHtml_RendersBlocksAndEscapes()
    {
        var html = HtmlReportExporter.MarkdownToHtml(
            "# Заголовок\n\nДля **бухгалтерии**, контакт <admin@corp.ru>\n\n- пункт один\n- пункт два\n\n```\nкод\n```");

        Assert.Contains("<h1>Заголовок</h1>", html);
        Assert.Contains("<strong>бухгалтерии</strong>", html);
        // Пользовательский ввод экранируется — HTML-инъекция невозможна.
        // Сущность собирается конкатенацией, чтобы не путать источник с HTML-разметкой.
        Assert.Contains("&" + "lt;admin@corp.ru" + "&" + "gt;", html);
        Assert.DoesNotContain("<admin@corp.ru>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<li>пункт один</li>", html);
        Assert.Contains("<pre><code>", html);

        Assert.Equal(string.Empty, HtmlReportExporter.MarkdownToHtml(null));
        Assert.Equal(string.Empty, HtmlReportExporter.MarkdownToHtml("   "));
    }

    [Fact]
    public void BuildDocument_ContainsNotesSection()
    {
        var doc = new HtmlReportDocument
        {
            AppName = "Workspace 1C",
            GeneratedAtText = "27.10.2026 21:00",
            ProfileName = "-",
            TotalCount = 1,
            Labels = new HtmlReportLabels
            {
                Header = "Отчёт по базам",
                NotesHeaderFormat = "Заметки ({0})",
                NotesEmpty = "Заметок нет"
            },
            NoteRows = new[]
            {
                new HtmlReportNoteRow
                {
                    Name = "Бухгалтерия",
                    GroupPath = "Финансы",
                    MarkdownHtml = HtmlReportExporter.MarkdownToHtml("## Контакт\nДля **отдела**")
                }
            }
        };

        var html = HtmlReportExporter.BuildDocument(doc);
        Assert.Contains("id=\"notes\"", html);
        Assert.Contains(">Заметки (1)</h2>", html);
        Assert.Contains("<h3>Бухгалтерия", html);
        Assert.Contains("<h2>Контакт</h2>", html);
        Assert.Contains("<strong>отдела</strong>", html);

        // Без заметок секция не создаётся.
        var without = new HtmlReportDocument { Labels = new HtmlReportLabels { NotesHeaderFormat = "Заметки ({0})" } };
        Assert.DoesNotContain("id=\"notes\"", HtmlReportExporter.BuildDocument(without));
    }

    // ---------- Перенос: JSON-снимок переносит только карточки и имена секретов ----------

    [Fact]
    public void JsonTransfer_ToTransfers_StripsSecretValues()
    {
        var notes = new[]
        {
            MakeNote("base-1", "Карточка", "пароль", "токен"),
            new InfobaseNote { InfobaseId = "", Markdown = "пустая" }
        };

        var transfers = InfobaseJsonTransfer.ToTransfers(notes);
        Assert.Single(transfers);
        Assert.Equal("base-1", transfers[0].InfobaseId);
        Assert.Equal("Карточка", transfers[0].Markdown);
        Assert.Equal(new[] { "пароль", "токен" }, transfers[0].SecretNames.OrderBy(n => n));

        var snapshot = InfobaseJsonTransfer.BuildSnapshot(new[] { new Infobase() }, new System.Collections.Generic.List<Group>(), notes);
        var json = InfobaseJsonTransfer.Serialize(snapshot);
        Assert.Contains("base-1", json);
        Assert.DoesNotContain("v2:encrypted", json); // значения секретов не уходят в файл

        var parsed = InfobaseJsonTransfer.Deserialize(json);
        Assert.NotNull(parsed);
        Assert.Single(parsed!.Notes);
    }

    // ---------- Контекстное меню: пункт «Заметки» в каталоге и стандартной компоновке ----------

    [Fact]
    public void ContextMenuCatalog_HasNotesItem()
    {
        var item = ContextMenuCatalog.FindByCommand(ContextMenuCommandKind.Notes);
        Assert.NotNull(item);
        Assert.Equal("Main.Notes", item!.TextKey);
        Assert.True(item.IsNew);

        var layout = ContextMenuLayouts.Default();
        var presence = new System.Collections.Generic.HashSet<string>();
        ContextMenuResolver.CollectItemIds(layout.Root, presence);
        Assert.Contains("Notes", presence);
    }

    // ---------- Модель: ключ базы ----------

    [Fact]
    public void InfobaseNote_KeyFor_UsesIdThenName()
    {
        var withId = new Infobase { Id = "  guid-123  ", Name = "Имя" };
        Assert.Equal("guid-123", InfobaseNote.KeyFor(withId));

        var withoutId = new Infobase { Id = "", Name = "База без ID" };
        Assert.Equal("База без ID", InfobaseNote.KeyFor(withoutId));

        Assert.Equal(string.Empty, InfobaseNote.KeyFor(null!));
        Assert.Equal(string.Empty, InfobaseNote.KeyFor(new Infobase()));
    }
}
