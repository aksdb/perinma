using System;
using System.Data;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace perinma.Views.Calendar.EventView;

public partial class SimpleTextViewModel : ViewModelBase
{
    public SimpleTextViewModel()
    {
        if (Design.IsDesignMode)
        {
            _label = "label";
            _content = "content";
        }
    }
    
    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _content = "";
}