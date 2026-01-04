using System;

namespace perinma.Storage.Models;

public abstract class AccountCredentials
{
    public required string Type { get; set; }
}

public class GoogleCredentials : AccountCredentials
{
    public string? AuthorizationCode { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string? TokenType { get; set; }
}

public class CalDavCredentials : AccountCredentials
{
    public required string ServerUrl { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}
