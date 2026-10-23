using System.Collections.ObjectModel;
using System.Globalization;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Общие списки баз» (выпуск 1.0.0.69): источники (имя + путь к
/// ibases.v8i или http-адрес), интервал автосинхронизации, сохранение и запуск
/// синхронизации. Само слияние выполняет главная ViewModel (через <c>syncNow</c>).
/// </summary>
public sealed class SharedListsViewModel : ViewModelBase
{
    private readonly Func<Task<string>> _syncNow;
    private string _intervalText;
    private string _statusText = string.Empty;
    private SharedListSourceRow? _selected;
    private bool _isBusy;

    public SharedListsViewModel(SharedListSettings settings, Func<Task<string>> syncNow)
    {
        _syncNow = syncNow;
        _intervalText = settings.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        foreach (var source in settings.Sources)
            Sources.Add(new SharedListSourceRow(source));
    }

    public ObservableCollection<SharedListSourceRow> Sources { get; } = new();

    public SharedListSourceRow? Selected { get => _selected; set => SetProperty(ref _selected, value); }

    public string IntervalText { get => _intervalText; set => SetProperty(ref _intervalText, value ?? string.Empty); }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !_isBusy;

    public void AddSource(string location)
    {
        var name = string.IsNullOrWhiteSpace(location)
            ? LocalizationManager.T("Shared.DefaultName")
            : System.IO.Path.GetFileNameWithoutExtension(location.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(name) || name.Equals("ibases", StringComparison.OrdinalIgnoreCase))
            name = LocalizationManager.T("Shared.DefaultName");
        var row = new SharedListSourceRow(new SharedListSource { Name = name, Location = location ?? string.Empty });
        Sources.Add(row);
        Selected = row;
    }

    public void RemoveSelected()
    {
        if (_selected is not null)
            Sources.Remove(_selected);
        Selected = null;
    }

    /// <summary>Проверка: ключ локализации ошибки или null.</summary>
    public string? Validate()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Sources)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Location))
                return "Shared.FillAll";
            if (!names.Add(row.Name.Trim()))
                return "Shared.DuplicateName";
        }
        return null;
    }

    public SharedListSettings BuildSettings() => new()
    {
        IntervalMinutes = int.TryParse(IntervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 ? i : 60,
        Sources = Sources.Select(r => r.ToSource()).ToList()
    };

    public bool Save()
    {
        var error = Validate();
        if (error is not null)
        {
            StatusText = LocalizationManager.T(error);
            return false;
        }
        SharedListService.Save(BuildSettings());
        StatusText = LocalizationManager.T("Shared.Saved");
        return true;
    }

    public async Task SyncNowAsync()
    {
        if (IsBusy || !Save())
            return;
        IsBusy = true;
        try
        {
            StatusText = LocalizationManager.T("Shared.Syncing");
            StatusText = await _syncNow();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Строка источника общего списка.</summary>
public sealed class SharedListSourceRow : ViewModelBase
{
    private string _name;
    private string _location;
    private bool _enabled;

    public SharedListSourceRow(SharedListSource source)
    {
        _name = source.Name ?? string.Empty;
        _location = source.Location ?? string.Empty;
        _enabled = source.Enabled;
    }

    public string Name { get => _name; set => SetProperty(ref _name, value ?? string.Empty); }

    public string Location { get => _location; set => SetProperty(ref _location, value ?? string.Empty); }

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public SharedListSource ToSource() => new() { Name = Name.Trim(), Location = Location.Trim(), Enabled = Enabled };
}
