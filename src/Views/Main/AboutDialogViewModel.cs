using System;
using System.Reflection;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace perinma.Views.Main;

public partial class AboutDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appName = "perinma";

    [ObservableProperty]
    private string _version = GetApplicationVersion();

    [ObservableProperty]
    private string _fullVersion = GetFullVersion();

    [ObservableProperty]
    private string _description = "Personal Information Manager";

    [ObservableProperty]
    private string _licenseText = LoadLicenseText();

    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "Unknown";
    }

    private static string GetFullVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informationalVersion ?? GetApplicationVersion();
    }

    private static string LoadLicenseText()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "perinma.LICENSE.txt";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return "License file not found.";
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            return $"Error loading license: {ex.Message}";
        }
    }
}
