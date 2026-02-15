using CommunityToolkit.Mvvm.ComponentModel;
using perinma.Models;

namespace perinma.Views.Calendar.EventView;

public partial class RichTextViewModel(RichText content) : ViewModelBase
{
    public RichText Content { get; } = content;

    public string? SimpleText => Content is RichText.SimpleText st ? st.value : null;

    public string? HtmlText => Content is RichText.HTML html ? html.value : null;

    public bool IsHtml => Content is RichText.HTML;
}
