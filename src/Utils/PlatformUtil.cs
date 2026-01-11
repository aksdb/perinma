using System.Diagnostics;
using System.Runtime.InteropServices;

namespace perinma.Utils;

public static class PlatformUtil
{
    /// Tries to open the given URL using the default mechanisms of the current OS.
    /// If this fails for whatever reason, it returns false.
    public static bool OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            } 
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
                return true;
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
                return true;
            }
        }
        catch
        {
            // Suppress errors and just consider this as "didn't succeed".
        }
        return false;
    }
}