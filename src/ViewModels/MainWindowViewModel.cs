using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using perinma.Storage;
using perinma.Views;

namespace perinma.ViewModels;

public partial class MainWindowViewModel(DatabaseService databaseService) : ViewModelBase
{
    public CalendarWeekViewModel CalendarWeekViewModel => CalendarWeekViewModel.Instance;
    
    #region Settings
    private SettingsWindow? _settingsWindow;
    
    [RelayCommand]
    private void ShowSettings()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }
        
        _settingsWindow = new SettingsWindow
        {
            DataContext = new SettingsViewModel(databaseService)
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }
    #endregion
}
