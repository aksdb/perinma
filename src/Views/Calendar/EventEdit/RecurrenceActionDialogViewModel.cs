using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using perinma.Models;

namespace perinma.Views.Calendar.EventEdit;

public sealed record RecurrenceActionOption(string Label, RecurringEventAction Action);

public partial class RecurrenceActionDialogViewModel
{
    public string Title { get; }
    public string EventTitle { get; }
    public IReadOnlyList<RecurrenceActionOption> Options { get; }

    public event Action<RecurringEventAction?>? CloseRequested;

    public RecurrenceActionDialogViewModel(string title, string eventTitle, IReadOnlyList<RecurrenceActionOption> options)
    {
        Title = title;
        EventTitle = eventTitle;
        Options = options;
    }

    [RelayCommand]
    private void SelectAction(RecurrenceActionOption? option)
    {
        CloseRequested?.Invoke(option?.Action);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(null);
    }
}
