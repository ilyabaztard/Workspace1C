namespace Workspace1C.Models;

/// <summary>
/// Пара «ключ/текст» для списка «Движок выгрузки» в мастере «Развернуть тестовую копию»
/// (п.10, 1.0.0.92; issue #79, 1.0.0.94). Используется в <c>ComboBox</c> на обеих
/// платформах (WPF и Avalonia). Переопределение <see cref="ToString"/> гарантирует,
/// что выпадающий список показывает человекочитаемый текст (например, «Конфигуратор»),
/// а не CLR-имя типа — иначе WPF рендерит «Workspace1C.TestDeployWindow+ScheduleOptionValue».
/// </summary>
public sealed class TestDeployEngineOption
{
    /// <param name="value">Стабильный ключ движка (Designer/Ibcmd).</param>
    /// <param name="text">Локализованный текст для отображения.</param>
    public TestDeployEngineOption(string value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>Ключ движка (сохраняется в <see cref="TestDeployScenario.EngineIndex"/>).</summary>
    public string Value { get; }

    /// <summary>Текст для отображения пользователю.</summary>
    public string Text { get; }

    /// <summary>Отображаемый текст вместо CLR-имени типа (issue #79).</summary>
    public override string ToString() => Text;
}
