using Avalonia.Controls;
using Avalonia.Interactivity;
using perinma.Views.Settings;

namespace perinma.Views.Settings;

public partial class SettingsWindow : Window
{
    private readonly GeneralSettingsView _generalSettingsView;
    private readonly AccountListView _accountListView;

    public SettingsWindow()
    {
        InitializeComponent();

        // Create view instances with their respective view models
        _generalSettingsView = new GeneralSettingsView
        {
            DataContext = new GeneralSettingsViewModel()
        };

        _accountListView = new AccountListView
        {
            DataContext = new AccountListViewModel()
        };

        // Set initial content
        ContentArea.Content = _generalSettingsView;
    }

    private void OnSettingsTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (SettingsTree.SelectedItem is not TreeViewItem selectedItem)
            return;

        var tag = selectedItem.Tag as string;

        ContentArea.Content = tag switch
        {
            "General" => _generalSettingsView,
            "Accounts" => _accountListView,
            _ => _generalSettingsView
        };
    }
}