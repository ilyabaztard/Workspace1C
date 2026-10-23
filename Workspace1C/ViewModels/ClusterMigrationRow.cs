using System.ComponentModel;
using System.Runtime.CompilerServices;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка чеклиста окна «Миграция между кластерами 1С» (issue #41, переработка):
/// информационная база кластера-источника с флажком переноса и статусами
/// источника/приёмника. Чистая модель без платформенных зависимостей — обе
/// платформы (WPF и Avalonia); UI только привязывается.
/// </summary>
public sealed class ClusterMigrationRow : INotifyPropertyChanged
{
    private bool _isChecked;
    private string _sourceStatus = string.Empty;
    private string _targetStatus = string.Empty;

    public ClusterMigrationRow(
        string name,
        string subtitle,
        string clusterName,
        string connectionString,
        RacInfobaseSummary source)
    {
        Name = name;
        Subtitle = subtitle;
        ClusterName = clusterName;
        ConnectionString = connectionString;
        Source = source;
    }

    /// <summary>Имя информационной базы (совпадает с <c>Ref</c> строки подключения).</summary>
    public string Name { get; }

    /// <summary>Подпись строки: описание / тип СУБД и сервер / имя БД в СУБД.</summary>
    public string Subtitle { get; }

    /// <summary>Имя кластера, из которого переносится база (колонка «Кластер»).</summary>
    public string ClusterName { get; }

    /// <summary>Строка подключения базы 1С (<c>Srvr="host[:port]";Ref="name"</c>) — колонка «Подключение».</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Исходная запись базы кластера («infobase summary list» источника): нужна миграции
    /// — идентификатор базы для удаления из старого кластера и параметры создания
    /// в новом (см. <see cref="ClusterMigrationService.BuildCreateRequest"/>).
    /// </summary>
    public RacInfobaseSummary Source { get; }

    /// <summary>Отмечена ли строка для переноса.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Статус источника (колонка «Статус источника»): причина, по которой база не
    /// импортируется в список приложения («уже есть в списке»); пустая строка — база
    /// новая. Для переноса статус источника не помеха — база переносится как есть.
    /// </summary>
    public string SourceStatus
    {
        get => _sourceStatus;
        set
        {
            if (!string.Equals(_sourceStatus, value ?? string.Empty, StringComparison.Ordinal))
            {
                _sourceStatus = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// Статус приёмника (колонка «Статус приёмника»): «Есть в кластере», если база
    /// с таким именем уже существует в выбранном кластере-приёмнике; пустая строка —
    /// базы в приёмнике нет (перенос возможен). Проставляется кнопкой «Получить статусы».
    /// </summary>
    public string TargetStatus
    {
        get => _targetStatus;
        set
        {
            if (!string.Equals(_targetStatus, value ?? string.Empty, StringComparison.Ordinal))
            {
                _targetStatus = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName!));
}