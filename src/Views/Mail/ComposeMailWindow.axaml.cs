using Avalonia.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace perinma.Views.Mail;

public partial class ComposeMailWindow : AtomUI.Desktop.Controls.Window
{
    public ComposeMailWindow()
    {
        InitializeComponent();
    }

    protected override async void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        var editorView = this.FindControl<ComposeMailEditorView>("EditorView");
        if (editorView != null)
            await editorView.FocusEditorAsync();
    }

    private async void OnAddAttachmentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComposeMailViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("All files") { Patterns = ["*.*"] }
            ]
        });

        var paths = GetLocalPaths(files);
        if (paths.Count == 0)
            return;

        await viewModel.AddAttachmentsAsync(paths, isInline: false);
    }

    private async void OnInsertImageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ComposeMailViewModel viewModel)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.svg"] }
            ]
        });

        var paths = GetLocalPaths(files);
        if (paths.Count == 0)
            return;

        var editorView = this.FindControl<ComposeMailEditorView>("EditorView");
        var attachments = await viewModel.AddAttachmentsAsync(paths, isInline: true);
        if (editorView == null)
            return;

        foreach (var attachment in attachments.Where(attachment => !string.IsNullOrWhiteSpace(attachment.ContentPath)))
            await editorView.InsertImageAsync(attachment.ContentPath!, attachment.FileName);

        await editorView.FocusEditorAsync();
    }

    private static List<string> GetLocalPaths(IEnumerable<IStorageFile> files)
        => files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToList();
}
