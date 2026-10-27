namespace Workspace1C.Models;

/// <summary>
/// Чем выполняется выгрузка сценария резервирования (выпуск 1.0.0.84).
/// </summary>
public enum BackupEngine
{
    /// <summary>Пакетный режим «Конфигуратора» (1cv8 DESIGNER) — как раньше.</summary>
    Designer,

    /// <summary>Утилита автономного сервера ibcmd: без окна и лицензии конфигуратора.</summary>
    Ibcmd
}
