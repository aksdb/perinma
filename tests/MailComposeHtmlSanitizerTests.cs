using NUnit.Framework;
using perinma.Services;

namespace tests;

[TestFixture]
public class MailComposeHtmlSanitizerTests
{
    [Test]
    public void Sanitize_RemovesRemoteImages_ButKeepsLocalImages_AndBuildsPlainText()
    {
        const string html = "<p>Hello <strong>world</strong></p><img src=\"https://example.com/tracker.png\"><img src=\"file:///tmp/local.png\" alt=\"local\"><blockquote>Quoted text</blockquote>";

        var sanitized = MailComposeHtmlSanitizer.Sanitize(html, allowLocalFileReferences: true);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized.Html, Does.Not.Contain("https://example.com/tracker.png"));
            Assert.That(sanitized.Html, Does.Contain("file:///tmp/local.png"));
            Assert.That(sanitized.Html, Does.Contain("<strong>world</strong>"));
            Assert.That(sanitized.PlainText, Does.Contain("Hello world"));
            Assert.That(sanitized.PlainText, Does.Contain("Quoted text"));
            Assert.That(sanitized.HasBlockedContent, Is.True);
        });
    }
}
