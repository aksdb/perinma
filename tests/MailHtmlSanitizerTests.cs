using NUnit.Framework;
using perinma.Services;

namespace tests;

[TestFixture]
public class MailHtmlSanitizerTests
{
    [Test]
    public void Sanitize_BlocksScriptsAndRemoteImagesByDefault()
    {
        const string html = """
            <html>
              <body>
                <script>alert('xss')</script>
                <img src="https://cdn.example.com/tracker.png" alt="tracker">
                <a href="https://example.com">link</a>
              </body>
            </html>
            """;

        var sanitized = MailHtmlSanitizer.Sanitize(html, allowExternalResources: false);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized.DocumentHtml, Does.Not.Contain("<script"));
            Assert.That(sanitized.DocumentHtml, Does.Not.Contain("https://cdn.example.com/tracker.png"));
            Assert.That(sanitized.DocumentHtml, Does.Contain("https://example.com"));
            Assert.That(sanitized.DocumentHtml, Does.Contain("script-src 'none'"));
            Assert.That(sanitized.HasBlockedRemoteContent, Is.True);
        });
    }

    [Test]
    public void Sanitize_AllowsRemoteImagesWhenExplicitlyEnabled()
    {
        const string html = "<img src=\"https://cdn.example.com/logo.png\">";

        var sanitized = MailHtmlSanitizer.Sanitize(html, allowExternalResources: true);

        Assert.Multiple(() =>
        {
            Assert.That(sanitized.DocumentHtml, Does.Contain("https://cdn.example.com/logo.png"));
            Assert.That(sanitized.DocumentHtml, Does.Contain("img-src data: cid: https: http:"));
            Assert.That(sanitized.HasBlockedRemoteContent, Is.False);
        });
    }
}
