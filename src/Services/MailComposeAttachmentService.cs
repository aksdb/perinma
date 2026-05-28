using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using perinma.Models;

namespace perinma.Services;

public sealed class MailComposeAttachmentService
{
    private readonly string _rootDirectory;

    public MailComposeAttachmentService(string? rootDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            rootDirectory = Path.Combine(appData, "perinma", "mail-compose");
        }

        _rootDirectory = rootDirectory;
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    public string GetDraftDirectory(string draftId) => Path.Combine(_rootDirectory, draftId);

    public async Task<MailComposeAttachment> StageFileAsync(
        string draftId,
        string sourcePath,
        bool isInline = false,
        string? contentId = null,
        int sortOrder = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path is required.", nameof(sourcePath));
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Attachment source file was not found.", sourcePath);

        var fileName = Path.GetFileName(sourcePath);
        var mimeType = GetMimeType(fileName);
        var extension = Path.GetExtension(fileName);
        await using var sourceStream = File.OpenRead(sourcePath);
        return await StageStreamCoreAsync(
            draftId,
            fileName,
            extension,
            mimeType,
            sourceStream,
            isInline,
            contentId,
            sortOrder,
            cancellationToken);
    }

    public async Task<MailComposeAttachment> StageInlineBytesAsync(
        string draftId,
        string fileName,
        string mimeType,
        byte[] content,
        string? contentId = null,
        int sortOrder = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(mimeType))
            throw new ArgumentException("Mime type is required.", nameof(mimeType));

        var extension = Path.GetExtension(fileName);
        await using var sourceStream = new MemoryStream(content, writable: false);
        return await StageStreamCoreAsync(
            draftId,
            fileName,
            extension,
            mimeType,
            sourceStream,
            isInline: true,
            contentId,
            sortOrder,
            cancellationToken);
    }

    public Task DeleteDraftFilesAsync(string draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var draftDirectory = GetDraftDirectory(draftId);
        if (Directory.Exists(draftDirectory))
            Directory.Delete(draftDirectory, recursive: true);
        return Task.CompletedTask;
    }

    private async Task<MailComposeAttachment> StageStreamCoreAsync(
        string draftId,
        string fileName,
        string extension,
        string mimeType,
        Stream sourceStream,
        bool isInline,
        string? contentId,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(draftId))
            throw new ArgumentException("Draft id is required.", nameof(draftId));

        var draftDirectory = GetDraftDirectory(draftId);
        Directory.CreateDirectory(draftDirectory);

        var attachmentId = Guid.NewGuid();
        var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(safeBaseName))
            safeBaseName = "attachment";

        var resolvedExtension = string.IsNullOrWhiteSpace(extension)
            ? GuessExtension(mimeType)
            : extension;

        var targetPath = Path.Combine(draftDirectory, $"{attachmentId:N}-{safeBaseName}{resolvedExtension}");
        string hash;
        long size;

        using (var sha256 = SHA256.Create())
        await using (var targetStream = File.Create(targetPath))
        using (var cryptoStream = new CryptoStream(targetStream, sha256, CryptoStreamMode.Write, leaveOpen: true))
        {
            await sourceStream.CopyToAsync(cryptoStream, cancellationToken);
            await cryptoStream.FlushAsync(cancellationToken);
            cryptoStream.FlushFinalBlock();
            await targetStream.FlushAsync(cancellationToken);
            size = targetStream.Length;
            hash = Convert.ToHexString(sha256.Hash ?? []);
        }

        return new MailComposeAttachment
        {
            Id = attachmentId,
            FileName = fileName,
            MimeType = mimeType,
            Size = size,
            IsInline = isInline,
            ContentId = isInline
                ? string.IsNullOrWhiteSpace(contentId)
                    ? CreateContentId(fileName)
                    : contentId
                : contentId,
            ContentPath = targetPath,
            Hash = hash,
            SortOrder = sortOrder
        };
    }

    private static string CreateContentId(string fileName)
    {
        var suffix = Path.GetExtension(fileName).TrimStart('.');
        var token = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(suffix)
            ? $"{token}@perinma.local"
            : $"{token}.{suffix}@perinma.local";
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidCharacter, '_');
        return value;
    }

    private static string GuessExtension(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "text/plain" => ".txt",
        "text/html" => ".html",
        "application/pdf" => ".pdf",
        _ => string.Empty
    };

    private static string GetMimeType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".htm" or ".html" => "text/html",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
