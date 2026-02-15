using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class AttachmentsViewModel(List<CalendarEventAttachment> attachments) : ViewModelBase
{
    public ObservableCollection<AttachmentItemViewModel> Attachments { get; } = 
        [..attachments.Select(a => new AttachmentItemViewModel(a))];
}

public partial class AttachmentItemViewModel(CalendarEventAttachment attachment) : ViewModelBase
{
    [ObservableProperty]
    private string _title = attachment.Title;

    [ObservableProperty]
    private string _url = attachment.Url;
}
