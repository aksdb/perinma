using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using perinma.Services;

namespace tests;

[TestFixture]
public class MailComposeAttachmentServiceTests
{
    [Test]
    public async Task StageFileAsync_CopiesFileAndComputesHash()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "perinma-compose-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var sourcePath = Path.Combine(rootDirectory, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "hello compose");
        var service = new MailComposeAttachmentService(rootDirectory);

        try
        {
            var attachment = await service.StageFileAsync("draft-1", sourcePath);

            Assert.Multiple(() =>
            {
                Assert.That(attachment.FileName, Is.EqualTo("source.txt"));
                Assert.That(attachment.MimeType, Is.EqualTo("text/plain"));
                Assert.That(attachment.ContentPath, Is.Not.Null.And.Not.EqualTo(sourcePath));
                Assert.That(File.Exists(attachment.ContentPath), Is.True);
                Assert.That(attachment.Hash, Is.Not.Null.And.Not.Empty);
                Assert.That(attachment.Size, Is.GreaterThan(0));
            });
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Test]
    public async Task StageInlineBytesAsync_CreatesInlineAttachmentAndCleanupRemovesDraftDirectory()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "perinma-compose-tests", Guid.NewGuid().ToString("N"));
        var service = new MailComposeAttachmentService(rootDirectory);

        try
        {
            var attachment = await service.StageInlineBytesAsync(
                "draft-inline",
                "image.png",
                "image/png",
                [1, 2, 3, 4]);

            var draftDirectory = service.GetDraftDirectory("draft-inline");

            Assert.Multiple(() =>
            {
                Assert.That(attachment.IsInline, Is.True);
                Assert.That(attachment.ContentId, Does.Contain("@perinma.local"));
                Assert.That(attachment.ContentPath, Is.Not.Null);
                Assert.That(File.Exists(attachment.ContentPath), Is.True);
                Assert.That(Directory.Exists(draftDirectory), Is.True);
            });

            await service.DeleteDraftFilesAsync("draft-inline");
            Assert.That(Directory.Exists(draftDirectory), Is.False);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }
}
