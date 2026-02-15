using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Microsoft.Extensions.DependencyInjection;
using perinma.Views;

namespace perinma;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            Control? control = null;
            try
            {
                if (App.Services != null)
                {
                    control = (Control)App.Services.GetService(type)!;
                }
                
                if (control == null)
                {
                    control = (Control)Activator.CreateInstance(type)!;
                }
            }
            catch
            {
                control = (Control)Activator.CreateInstance(type)!;
            }
            
            return control;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
