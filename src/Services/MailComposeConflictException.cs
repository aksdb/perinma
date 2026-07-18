using System;

namespace perinma.Services;

public sealed class MailComposeConflictException : InvalidOperationException
{
    public MailComposeConflictException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
