using System;

namespace Workspace1C.Models;

/// <summary>
/// Установленная программная лицензия 1С — строка разбора вывода утилит
/// «ring license list» / «licensetool» (п.5 «Мониторинг лицензий», выпуск 1.0.0.87).
/// Вывод утилит зависит от платформы и локали окружения, поэтому парсер
/// <see cref="Services.RingLicenseParser"/> допускает оба формата (таблицу и блоки)
/// и заполняет только распознанные поля; остальная информация остаётся в
/// <see cref="RawText"/> для отображения «как есть».
/// </summary>
public sealed class ProgramLicense
{
    /// <summary>Серийный номер программной лицензии (например <c>12345678-1234-1234-1234-123456789012</c>); пусто — не распознан.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Тип лицензии по выводу утилиты (например «клиентская», «сервер», «server», «client», «CONFIG-FULL»); пусто — не распознан.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Версия платформы/сборки, для которой выдана лицензия (8.3.x и т.п.); пусто — не распознано.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Модуль/назначение лицензии (строка вывода утилиты).</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Пользователь/владелец лицензии (строка вывода утилиты).</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Состояние лицензии (строка вывода утилиты: «active», «used» и т.п.); пусто — не распознано.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Срок действия лицензии (если утилита его выводит); null — не распознан.</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// Количество лицензий/рабочих мест, которое даёт серийный номер
    /// (0 — утилита не вывела; для расчёта сводки считается 1).
    /// </summary>
    public int LicenseCount { get; set; }

    /// <summary>Сырая строка вывода утилиты (для диагностики и отображения «как есть»).</summary>
    public string RawText { get; set; } = string.Empty;
}
