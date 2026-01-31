using System;
using System.Linq;
using System.Xml.Linq;

namespace perinma.Services.CalDAV;

/// <summary>
/// Represents a privilege in WebDAV ACL (RFC 3744).
/// </summary>
public record WebDavPrivilege
{
    /// <summary>
    /// Namespace of the privilege (e.g., "DAV:" or "urn:ietf:params:xml:ns:caldav").
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    /// Name of the privilege (e.g., "read", "write", "schedule-deliver").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Returns the full privilege string including namespace.
    /// </summary>
    public string FullName => $"{Namespace}{Name}";

    /// <summary>
    /// Common WebDAV privileges.
    /// </summary>
    public static class WebDav
    {
        public const string Namespace = "DAV:";

        public static readonly WebDavPrivilege Read = new() { Namespace = Namespace, Name = "read" };
        public static readonly WebDavPrivilege Write = new() { Namespace = Namespace, Name = "write" };
        public static readonly WebDavPrivilege ReadAcl = new() { Namespace = Namespace, Name = "read-acl" };
        public static readonly WebDavPrivilege WriteAcl = new() { Namespace = Namespace, Name = "write-acl" };
        public static readonly WebDavPrivilege WriteProperties = new() { Namespace = Namespace, Name = "write-properties" };
        public static readonly WebDavPrivilege WriteContent = new() { Namespace = Namespace, Name = "write-content" };
        public static readonly WebDavPrivilege Bind = new() { Namespace = Namespace, Name = "bind" };
        public static readonly WebDavPrivilege Unbind = new() { Namespace = Namespace, Name = "unbind" };
        public static readonly WebDavPrivilege Unlock = new() { Namespace = Namespace, Name = "unlock" };
    }

    /// <summary>
    /// CalDAV-specific scheduling privileges (RFC 6638).
    /// </summary>
    public static class CalDav
    {
        public const string Namespace = "urn:ietf:params:xml:ns:caldav";

        public static readonly WebDavPrivilege ScheduleDeliver = new() { Namespace = Namespace, Name = "schedule-deliver" };
        public static readonly WebDavPrivilege ScheduleDeliverInvite = new() { Namespace = Namespace, Name = "schedule-deliver-invite" };
        public static readonly WebDavPrivilege ScheduleDeliverReply = new() { Namespace = Namespace, Name = "schedule-deliver-reply" };
        public static readonly WebDavPrivilege ScheduleQueryFreebusy = new() { Namespace = Namespace, Name = "schedule-query-freebusy" };
        public static readonly WebDavPrivilege ScheduleSend = new() { Namespace = Namespace, Name = "schedule-send" };
        public static readonly WebDavPrivilege ScheduleSendInvite = new() { Namespace = Namespace, Name = "schedule-send-invite" };
        public static readonly WebDavPrivilege ScheduleSendReply = new() { Namespace = Namespace, Name = "schedule-send-reply" };
        public static readonly WebDavPrivilege ScheduleSendFreebusy = new() { Namespace = Namespace, Name = "schedule-send-freebusy" };
        public static readonly WebDavPrivilege ReadFreeBusy = new() { Namespace = Namespace, Name = "read-free-busy" };
    }
}

/// <summary>
/// Represents a principal in WebDAV ACL (RFC 3744).
/// </summary>
public record WebDavPrincipal(
    string? Href,
    bool IsAll,
    bool IsAuthenticated,
    bool IsUnauthenticated,
    bool IsSelf,
    string? PropertyPrincipal)
{
    /// <summary>
    /// Creates a URL-based principal.
    /// </summary>
    public static WebDavPrincipal FromHref(string href) => new WebDavPrincipal(href, false, false, false, false, null);

    /// <summary>
    /// Creates a pseudo-principal.
    /// </summary>
    public static WebDavPrincipal All() => new WebDavPrincipal(null, true, false, false, false, null);

    /// <summary>
    /// Creates an authenticated users principal.
    /// </summary>
    public static WebDavPrincipal Authenticated() => new WebDavPrincipal(null, false, true, false, false, null);

    /// <summary>
    /// Creates an unauthenticated users principal.
    /// </summary>
    public static WebDavPrincipal Unauthenticated() => new WebDavPrincipal(null, false, false, true, false, null);

    /// <summary>
    /// Creates a property-based principal (e.g., "owner").
    /// </summary>
    public static WebDavPrincipal FromProperty(string property) => new WebDavPrincipal(null, false, false, false, false, property);
}

/// <summary>
/// Represents an Access Control Entry (ACE) in WebDAV ACL (RFC 3744).
/// </summary>
public record WebDavAce(
    WebDavPrincipal Principal,
    bool IsInverted,
    bool IsGrant,
    bool IsProtected,
    string? InheritedFrom,
    WebDavPrivilege[] Privileges);

/// <summary>
/// Represents a WebDAV Access Control List (RFC 3744).
/// </summary>
public record WebDavAcl(WebDavAce[] Aces);

/// <summary>
/// Represents the current user's privileges for a resource (DAV:current-user-privilege-set).
/// </summary>
public record WebDavCurrentUserPrivilegeSet(WebDavPrivilege[] Privileges)
{
    /// <summary>
    /// Checks if the current user has a specific privilege.
    /// </summary>
    public bool HasPrivilege(string ns, string name)
    {
        return Privileges.Any(p => p.Namespace == ns && p.Name == name);
    }

    /// <summary>
    /// Checks if the current user can read the resource.
    /// </summary>
    public bool CanRead => HasPrivilege(WebDavPrivilege.WebDav.Namespace, "read");

    /// <summary>
    /// Checks if the current user can write the resource.
    /// </summary>
    public bool CanWrite => HasPrivilege(WebDavPrivilege.WebDav.Namespace, "write");

    /// <summary>
    /// Checks if the current user can read the ACL.
    /// </summary>
    public bool CanReadAcl => HasPrivilege(WebDavPrivilege.WebDav.Namespace, "read-acl");

    /// <summary>
    /// Checks if the current user can modify the ACL.
    /// </summary>
    public bool CanWriteAcl => HasPrivilege(WebDavPrivilege.WebDav.Namespace, "write-acl");
}

/// <summary>
/// Parser for WebDAV ACL data structures (RFC 3744).
/// </summary>
public static class WebDavAclParser
{
    private const string DavNamespace = "DAV:";
    private const string CalDavNamespace = "urn:ietf:params:xml:ns:caldav";

    /// <summary>
    /// Parses an ACL element from XML.
    /// </summary>
    public static WebDavAcl ParseAcl(XElement aclElement)
    {
        var d = aclElement.GetNamespaceOfPrefix("D") ?? XNamespace.Get(DavNamespace);
        var aces = aclElement.Elements(d + "ace")
            .Select(ParseAce)
            .ToArray();
        return new WebDavAcl(aces);
    }

    private static WebDavAce ParseAce(XElement aceElement)
    {
        var d = aceElement.GetNamespaceOfPrefix("D") ?? XNamespace.Get(DavNamespace);

        // Parse principal (or inverted principal)
        var principalElement = aceElement.Element(d + "principal")
            ?? aceElement.Element(d + "invert")?.Element(d + "principal");
        var isInverted = aceElement.Element(d + "invert") is not null;
        var principal = ParsePrincipal(principalElement!);

        // Parse grant/deny
        var grantElement = aceElement.Element(d + "grant");
        var denyElement = aceElement.Element(d + "deny");
        var isGrant = grantElement is not null;
        var privilegesElement = isGrant ? grantElement : denyElement;

        var privileges = privilegesElement!.Elements(d + "privilege")
            .Select(ParsePrivilege)
            .ToArray();

        // Parse protected
        var isProtected = aceElement.Element(d + "protected") is not null;

        // Parse inherited
        var inheritedElement = aceElement.Element(d + "inherited")?.Element(d + "href");
        var inheritedFrom = inheritedElement?.Value;

        return new WebDavAce(
            principal,
            isInverted,
            isGrant,
            isProtected,
            inheritedFrom,
            privileges
        );
    }

    private static WebDavPrincipal ParsePrincipal(XElement principalElement)
    {
        var d = principalElement.GetNamespaceOfPrefix("D") ?? XNamespace.Get(DavNamespace);

        var href = principalElement.Element(d + "href")?.Value;
        var all = principalElement.Element(d + "all") is not null;
        var authenticated = principalElement.Element(d + "authenticated") is not null;
        var unauthenticated = principalElement.Element(d + "unauthenticated") is not null;
        var self = principalElement.Element(d + "self") is not null;

        var propertyElement = principalElement.Elements()
            .FirstOrDefault(e =>
                e.Name.LocalName != "href" &&
                e.Name.LocalName != "all" &&
                e.Name.LocalName != "authenticated" &&
                e.Name.LocalName != "unauthenticated" &&
                e.Name.LocalName != "self");

        return new WebDavPrincipal(
            href,
            all,
            authenticated,
            unauthenticated,
            self,
            propertyElement?.Name.LocalName
        );
    }

    private static WebDavPrivilege ParsePrivilege(XElement privilegeElement)
    {
        var child = privilegeElement.Elements().First();
        var ns = child.Name.NamespaceName;
        var name = child.Name.LocalName;

        return new WebDavPrivilege
        {
            Namespace = ns,
            Name = name
        };
    }

    /// <summary>
    /// Parses the current-user-privilege-set element from XML.
    /// </summary>
    public static WebDavCurrentUserPrivilegeSet ParseCurrentUserPrivilegeSet(XElement element)
    {
        var d = element.GetNamespaceOfPrefix("D") ?? XNamespace.Get(DavNamespace);
        var privileges = element.Elements(d + "privilege")
            .Select(ParsePrivilege)
            .ToArray();
        return new WebDavCurrentUserPrivilegeSet(privileges);
    }

    /// <summary>
    /// Builds an ACL XML document from a WebDavAcl object.
    /// </summary>
    public static string BuildAclXml(WebDavAcl acl)
    {
        var d = XNamespace.Get(DavNamespace);
        var c = XNamespace.Get(CalDavNamespace);

        var aclElement = new XElement(d + "acl",
            acl.Aces.Select(BuildAce)
        );

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), aclElement);
        using var writer = new System.IO.StringWriter();
        doc.Save(writer);
        return writer.ToString();
    }

    private static XElement BuildAce(WebDavAce ace)
    {
        var d = XNamespace.Get(DavNamespace);

        var principalElement = ace.IsInverted
            ? new XElement(d + "invert", BuildPrincipal(ace.Principal))
            : BuildPrincipal(ace.Principal);

        var grantOrDeny = new XElement(d + (ace.IsGrant ? "grant" : "deny"),
            ace.Privileges.Select(BuildPrivilege)
        );

        var aceElement = new XElement(d + "ace", principalElement, grantOrDeny);

        if (ace.IsProtected)
        {
            aceElement.Add(new XElement(d + "protected"));
        }

        if (ace.InheritedFrom != null)
        {
            aceElement.Add(new XElement(d + "inherited",
                new XElement(d + "href", ace.InheritedFrom)
            ));
        }

        return aceElement;
    }

    private static XElement BuildPrincipal(WebDavPrincipal principal)
    {
        var d = XNamespace.Get(DavNamespace);

        if (principal.Href != null)
        {
            return new XElement(d + "principal",
                new XElement(d + "href", principal.Href)
            );
        }
        else if (principal.IsAll)
        {
            return new XElement(d + "principal",
                new XElement(d + "all")
            );
        }
        else if (principal.IsAuthenticated)
        {
            return new XElement(d + "principal",
                new XElement(d + "authenticated")
            );
        }
        else if (principal.IsUnauthenticated)
        {
            return new XElement(d + "principal",
                new XElement(d + "unauthenticated")
            );
        }
        else if (principal.IsSelf)
        {
            return new XElement(d + "principal",
                new XElement(d + "self")
            );
        }
        else if (principal.PropertyPrincipal != null)
        {
            return new XElement(d + "principal",
                new XElement(d + principal.PropertyPrincipal)
            );
        }

        throw new ArgumentException("Invalid principal: no valid type specified", nameof(principal));
    }

    private static XElement BuildPrivilege(WebDavPrivilege privilege)
    {
        var d = XNamespace.Get(DavNamespace);
        return new XElement(d + "privilege",
            new XElement(XNamespace.Get(privilege.Namespace) + privilege.Name)
        );
    }
}
