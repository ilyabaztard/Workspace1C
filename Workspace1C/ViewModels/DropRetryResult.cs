namespace Workspace1C.ViewModels;

/// <summary>
/// Решение пользователя в диалоге повтора удаления информационной базы из кластера
/// (issue #69): «Повторить попытку» с учётными данными администратора ИБ либо
/// «Отмена». Логин и пароль передаются между показами диалога (предзаполняются,
/// комментарий 7OH от 2026-10-26: «Пароль сохранять между попытками»).
/// <see cref="TerminateSessions"/> — пользователь попросил перед повтором завершить
/// активные сеансы/соединения базы (кнопка «Завершить сеансы и повторить» при
/// ошибке удаления из-за занятой базы).
/// </summary>
public sealed record DropRetryResult(bool Retry, string? User, string? Password, bool TerminateSessions = false)
{
    /// <summary>Результат «Отмена»: повторная попытка удаления не выполняется.</summary>
    public static DropRetryResult Cancel { get; } = new(false, null, null);
}