using CredentialStore.Interop.Linux;
using CredentialStore.Interop.MacOS;
using CredentialStore.Interop.Windows;

namespace CredentialStore;

public static class CredentialStore
{
    public static ICredentialStore Create(string @namespace)
    {
        if (PlatformUtils.IsLinux())
        {
            return new SecretServiceCollection(@namespace);
        }
        
        if (PlatformUtils.IsMacOS())
        {
            return new MacOSKeychain(@namespace);
        }

        if (PlatformUtils.IsWindows())
        {
            return new WindowsCredentialManager(@namespace);
        }
        
        throw new PlatformNotSupportedException();
    } 
}