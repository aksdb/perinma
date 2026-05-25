using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Services;

public partial class DebugFeaturesService(SettingsService? settingsService = null) : ObservableObject
{
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isDebuggingEnabled = SettingsService.Defaults.DebuggingEnabled;

    public async Task LoadAsync()
    {
        if (_isLoaded || settingsService == null)
            return;

        IsDebuggingEnabled = await settingsService.GetDebuggingEnabledAsync();
        _isLoaded = true;
    }

    public async Task SetDebuggingEnabledAsync(bool value)
    {
        _isLoaded = true;
        IsDebuggingEnabled = value;

        if (settingsService != null)
            await settingsService.SetDebuggingEnabledAsync(value);
    }

    public Task ToggleDebuggingAsync() => SetDebuggingEnabledAsync(!IsDebuggingEnabled);
}
