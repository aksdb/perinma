using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Services;

namespace perinma.Views.Settings;

public partial class GeneralSettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private bool _isLoading;

    [ObservableProperty]
    private int _autoSyncInterval = SettingsService.Defaults.AutoSyncInterval;

    public GeneralSettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        _isLoading = true;
        try
        {
            AutoSyncInterval = await _settingsService.GetAutoSyncIntervalAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load settings: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    partial void OnAutoSyncIntervalChanged(int value)
    {
        if (!_isLoading)
        {
            _ = _settingsService.SetAutoSyncIntervalAsync(value);
        }
    }
}
