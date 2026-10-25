# =============================================================================
#  Уведомление о релизе Workspace 1C 1.0.0.71 в Telegram
#  Черновик по образцу _send_telegram_v100070.ps1 (оригинал утрачен,
#  скрипт восстановлен по описанию плана: версия, список исправлений, ссылка).
#
#  ВАЖНО: запускать ТОЛЬКО ПОСЛЕ создания релиза v1.0.0.71 на GitHub.
#  Перед запуском заполните BOT_TOKEN и CHAT_ID (см. ниже).
# =============================================================================

# Токен бота и идентификатор чата/канала.
# Если значения заданы переменными окружения — они будут взяты оттуда.
$BotToken = $env:WORKSPACE1C_TG_BOT_TOKEN
$ChatId   = $env:WORKSPACE1C_TG_CHAT_ID

if ([string]::IsNullOrWhiteSpace($BotToken)) {
    # TODO: вставьте токен бота (например: '123456789:AA...')
    $BotToken = 'PASTE_BOT_TOKEN_HERE'
}
if ([string]::IsNullOrWhiteSpace($ChatId)) {
    # TODO: вставьте chat_id канала/чата (например: '-1001234567890')
    # Для канала можно указать и публичное имя: '@Workspace1C'
    $ChatId = 'PASTE_CHAT_ID_HERE'
}

$ReleaseVersion = '1.0.0.71'
$ReleaseUrl     = 'https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.0.71'

# Текст сообщения: версия, краткий список ключевых исправлений, ссылка на релиз.
$Message = @"
Workspace 1C $ReleaseVersion — новый выпуск (Windows и Linux).

Исправлено:
• Меню \"Утилиты\": убран лишний пункт «Блокировка сеансов ИБ» (#66)
• Подменю «Новинки программы» не скрывается после размещения пунктов (#65)
• Команды Telegram-бота принимаются из тем/подгрупп канала (#64)
• Публикация: обнаружение инстансов Apache, читаемые поля ввода (#63)
• Читаемые заголовки в тёмной теме (#61)
• Миграция: имя БД переносится автоматически (#41)

Добавлено:
• Правка структуры меню в трее (редактор, стиль блоков/подменю) (#62)

Скачать: $ReleaseUrl
"@

# Отправка через Bot API (метод sendMessage).
$Uri = "https://api.telegram.org/bot$BotToken/sendMessage"
$Body = @{
    chat_id    = $ChatId
    text       = $Message
    disable_web_page_preview = $false
}

Write-Host "Отправляю уведомление о релизе v$ReleaseVersion в чат $ChatId ..."
try {
    $Response = Invoke-RestMethod -Uri $Uri -Method Post -Body $Body
    if ($Response.ok) {
        Write-Host "OK: сообщение отправлено (message_id=$($Response.result.message_id))."
    }
    else {
        Write-Error "Telegram вернул ошибку: $($Response.description)"
        exit 1
    }
}
catch {
    Write-Error "Не удалось отправить сообщение: $($_.Exception.Message)"
    exit 1
}