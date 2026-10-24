using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Юнит-тесты фикса issue #64 «Команды телеги» (выпуск 1.0.0.71): команды Telegram-бота
/// принимаются из темы/подгруппы канала (message_thread_id). Проверяются: разбор апдейтов
/// (извлечение thread), разрешение чата с учётом темы (<see cref="TelegramBotCommandService.IsAllowedChat"/>)
/// и fallback на чат уведомлений.
/// </summary>
public sealed class TelegramBotCommandServiceTests
{
    // ---------------- ParseUpdates ----------------

    [Fact]
    public void ParseUpdates_MessageWithThreadId_ExtractsThreadId()
    {
        const string json =
            "{\"ok\":true,\"result\":[{\"update_id\":11,\"message\":" +
            "{\"chat\":{\"id\":-100123},\"message_thread_id\":45,\"is_topic_message\":true,\"text\":\"/status\"}}]}";
        var updates = TelegramBotCommandService.ParseUpdates(json);
        var command = Assert.Single(updates);
        Assert.Equal("-100123", command.ChatId);
        Assert.Equal("45", command.MessageThreadId);
        Assert.Equal("/status", command.Text);
    }

    [Fact]
    public void ParseUpdates_PlainMessage_NoThreadId_Null()
    {
        const string json =
            "{\"ok\":true,\"result\":[{\"update_id\":12,\"message\":" +
            "{\"chat\":{\"id\":-100123},\"text\":\"/status\"}}]}";
        var updates = TelegramBotCommandService.ParseUpdates(json);
        var command = Assert.Single(updates);
        Assert.Equal("-100123", command.ChatId);
        Assert.Null(command.MessageThreadId);
        Assert.Equal("/status", command.Text);
    }

    [Fact]
    public void ParseUpdates_NonMessageUpdate_HasNullThread()
    {
        const string json = "{\"ok\":true,\"result\":[{\"update_id\":13,\"callback_query\":{}}]}";
        var updates = TelegramBotCommandService.ParseUpdates(json);
        var command = Assert.Single(updates);
        Assert.Null(command.MessageThreadId);
        Assert.Equal(string.Empty, command.ChatId);
        Assert.Equal(string.Empty, command.Text);
    }

    // ---------------- IsAllowedChat ----------------

    [Fact]
    public void IsAllowedChat_NotificationChatWithThread_MatchingThread_Allowed()
    {
        // AllowedChatIds пуст → кандидат из настроек уведомлений: t.me/c/123/45 → chat_id -100123, тема 45.
        var settings = new TelegramBotSettings();
        Assert.True(TelegramBotCommandService.IsAllowedChat(settings, "t.me/c/123/45", "-100123", "45"));
    }

    [Fact]
    public void IsAllowedChat_SameChat_OtherThread_Denied()
    {
        var settings = new TelegramBotSettings();
        // Другая тема — отклоняется.
        Assert.False(TelegramBotCommandService.IsAllowedChat(settings, "t.me/c/123/45", "-100123", "99"));
        // Настройка с темой не разрешает основной чат (команда без темы) — только указанную тему.
        Assert.False(TelegramBotCommandService.IsAllowedChat(settings, "t.me/c/123/45", "-100123", null));
    }

    [Fact]
    public void IsAllowedChat_SettingWithoutThread_AnyThreadAllowed()
    {
        // Настройка t.me/c/123 без темы разрешает весь чат: основной чат и любые темы.
        var settings = new TelegramBotSettings { AllowedChatIds = "t.me/c/123" };
        Assert.True(TelegramBotCommandService.IsAllowedChat(settings, "42", "-100123", "45"));
        Assert.True(TelegramBotCommandService.IsAllowedChat(settings, "42", "-100123", null));
    }

    [Fact]
    public void IsAllowedChat_EmptyAllowedChats_FallsBackToNotificationChat()
    {
        var settings = new TelegramBotSettings();
        Assert.True(TelegramBotCommandService.IsAllowedChat(settings, "-100123", "-100123", null));
        Assert.False(TelegramBotCommandService.IsAllowedChat(settings, "-100123", "-100999", null));
        Assert.False(TelegramBotCommandService.IsAllowedChat(settings, null, "-100999", null));
    }

    [Fact]
    public void IsAllowedChat_NumericChatId_ComparesByValue()
    {
        // Числовое сравнение через SameChatId (нормализация через ParseChatTarget, Trim).
        Assert.True(TelegramBotCommandService.IsAllowedChat(
            new TelegramBotSettings { AllowedChatIds = "42, 43" }, "99", "43", null));
        Assert.True(TelegramBotCommandService.IsAllowedChat(
            new TelegramBotSettings { AllowedChatIds = " -1001234567890 " }, "99", "-1001234567890", null));
        Assert.False(TelegramBotCommandService.IsAllowedChat(
            new TelegramBotSettings { AllowedChatIds = "42" }, "99", "43", null));
    }
}
