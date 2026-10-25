namespace Workspace1C.ViewModels;

/// <summary>
/// Решение пользователя в диалоге повтора удаления информационной базы из кластера
/// (issue #69): «Повторить попытку» с учётными данными администратора ИБ либо
/// «Отмена». Логин передаётся между показами диалога (предзаполняется), пароль —
/// вводится заново при каждой попытке.
/// </summary>
public sealed record DropRetryResult(bool Retry, string? User, string? Password)
{
    /// <summary>Результат «Отмена»: повторная попытка удаления не выполняется.</summary>
    public static DropRetryResult Cancel { get; } = new(false, null, null);
}