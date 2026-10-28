using System;

namespace Workspace1C.Services;

/// <summary>
/// Вспомогательный класс категорийного логирования (issue #80 «Настройка логирования»).
/// INFO-записи направлений пишутся в общий журнал приложения (<see cref="FileAppLogger"/>,
/// <c>logs/app-YYYYMMDD.log</c>) только если направление включено в <c>trace.json</c>
/// И включён общий выключатель <see cref="LogCategory.MasterOn"/> («CM_ON») —
/// фильтрацию выполняет <see cref="TraceFlags.IsLogCategoryEnabled"/>.
/// ОШИБКИ (ERROR) и ПРЕДУПРЕЖДЕНИЯ (WARN) от направлений не зависят и по-прежнему
/// пишутся напрямую через <see cref="IAppLogger"/> — важные сбои остаются видимыми
/// по умолчанию.
/// <para>
/// Категория добавляется в текст сообщения префиксом «[CM_…]», чтобы записи удобно
/// фильтровались в журнале, а по TraceFlagsFormat-конфигуруемой семантике читались
/// так же, как существующие диагностические флаги (CM_CLUSTERIMPORT и др.).
/// </para>
/// </summary>
public static class Log
{
    /// <summary>
    /// Пишет INFO-запись направления <paramref name="category"/> в журнал приложения,
    /// если направление включено (категория + общий выключатель CM_ON, issue #80).
    /// Возвращает <c>true</c>, если запись реально попала в журнал. Ошибки
    /// логирования не бросаются — диагностика не должна ронять рабочую операцию.
    /// </summary>
    /// <param name="category">Имя категории (например <see cref="LogCategory.BaseRun"/>).</param>
    /// <param name="message">Текст записи (без префикса категории — он добавляется сам).</param>
    public static bool Info(string category, string message)
    {
        if (!TraceFlags.IsLogCategoryEnabled(category))
            return false;
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Info($"[{category}] {message}");
            return true;
        }
        catch
        {
            // Логирование не должно ронять рабочую операцию.
            return false;
        }
    }

    /// <summary>
    /// Пишет INFO-запись направления <paramref name="category"/>, если направление
    /// включено; при выключенном направлении возвращает <c>true</c> без записи
    /// (аналог <see cref="Info"/>), а при недоступном логгере — <c>false</c>.
    /// Обёртка для мест, где вызов обязан «выглядеть как запись» (контракт
    /// «message => логгер.Info(...)» сервисов).
    /// </summary>
    public static bool TryInfo(string category, string message)
        => Info(category, message) || !TraceFlags.IsLogCategoryEnabled(category);
}
