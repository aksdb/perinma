using System.Runtime.InteropServices;
using CredentialStore.Interop.MacOS.Native;
using CredentialStore.Interop.Posix.Native;

namespace CredentialStore
{
    public static class PlatformUtils
    {
        public static bool IsDevBox()
        {
            if (!IsWindows())
            {
                return false;
            }

#if NETFRAMEWORK
            // Check for machine (HKLM) registry keys for Cloud PC indicators
            // Note that the keys are only found in the 64-bit registry view
            using (Microsoft.Win32.RegistryKey hklm64 =
 Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64))
            using (Microsoft.Win32.RegistryKey w365Key = hklm64.OpenSubKey(Constants.WindowsRegistry.HKWindows365Path))
            {
                if (w365Key is null)
                {
                    // No Windows365 key exists
                    return false;
                }

                object w365Value = w365Key.GetValue(Constants.WindowsRegistry.IsW365EnvironmentKeyName);
                string partnerValue = w365Key.GetValue(Constants.WindowsRegistry.W365PartnerIdKeyName)?.ToString();

                return w365Value is not null && Guid.TryParse(partnerValue, out Guid partnerId) && partnerId == Constants.DevBoxPartnerId;
            }
#else
            return false;
#endif
        }

        /// <summary>
        /// Returns true if the current process is running on an ARM processor.
        /// </summary>
        /// <returns>True if ARM(v6,hf) or ARM64, false otherwise</returns>
        public static bool IsArm()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.Arm:
                case Architecture.Arm64:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Check if the current Operating System is macOS.
        /// </summary>
        /// <returns>True if running on macOS, false otherwise.</returns>
        public static bool IsMacOS()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        }

        /// <summary>
        /// Check if the current Operating System is Windows.
        /// </summary>
        /// <returns>True if running on Windows, false otherwise.</returns>
        public static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// Check if the current Operating System is Linux-based.
        /// </summary>
        /// <returns>True if running on a Linux distribution, false otherwise.</returns>
        public static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        /// <summary>
        /// Check if the current Operating System is POSIX-compliant.
        /// </summary>
        /// <returns>True if running on a POSIX-compliant Operating System, false otherwise.</returns>
        public static bool IsPosix()
        {
            return IsMacOS() || IsLinux();
        }

        /// <summary>
        /// Ensure the current Operating System is macOS, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not macOS.</exception>
        public static void EnsureMacOS()
        {
            if (!IsMacOS())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is Windows, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not Windows.</exception>
        public static void EnsureWindows()
        {
            if (!IsWindows())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is Linux-based, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not Linux-based.</exception>
        public static void EnsureLinux()
        {
            if (!IsLinux())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is POSIX-compliant, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not POSIX-compliant.</exception>
        public static void EnsurePosix()
        {
            if (!IsPosix())
            {
                throw new PlatformNotSupportedException();
            }
        }

        public static bool IsElevatedUser()
        {
            if (IsWindows())
            {
#if NETFRAMEWORK
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#endif
            }
            else if (IsPosix())
            {
                return Unistd.geteuid() == 0;
            }

            return false;
        }

        #region Platform Entry Path Utils

        /// <summary>
        /// Get the native entry executable absolute path.
        /// </summary>
        /// <returns>Entry absolute path or null if there was an error.</returns>
        public static string GetNativeEntryPath()
        {
            try
            {
                if (IsWindows())
                {
                    return GetWindowsEntryPath();
                }

                if (IsMacOS())
                {
                    return GetMacOSEntryPath();
                }

                if (IsLinux())
                {
                    return GetLinuxEntryPath();
                }
            }
            catch
            {
                // If there are any issues getting the native entry path
                // we should not throw, and certainly not crash!
                // Just return null instead.
            }

            return null;
        }

        private static string GetLinuxEntryPath()
        {
            // Try to extract our native argv[0] from the original cmdline
            string cmdline = File.ReadAllText("/proc/self/cmdline");
            string argv0 = cmdline.Split('\0')[0];

            // argv[0] is an absolute file path
            if (Path.IsPathRooted(argv0))
            {
                return argv0;
            }

            string path = Path.GetFullPath(
                Path.Combine(Environment.CurrentDirectory, argv0)
            );

            // argv[0] is relative to current directory (./app) or a relative
            // name resolved from the current directory (subdir/app).
            // Note that we do NOT want to consider the case when it is just
            // a simple filename (argv[0] == "app") because that would actually
            // have been resolved from the $PATH instead (handled below)!
            if ((argv0.StartsWith("./") || argv0.IndexOf('/') > 0) && File.Exists(path))
            {
                return path;
            }

            // argv[0] is a name that was resolved from the $PATH
            string pathVar = Environment.GetEnvironmentVariable("PATH");
            if (pathVar != null)
            {
                string[] paths = pathVar.Split(':');
                foreach (string pathBase in paths)
                {
                    path = Path.Combine(pathBase, argv0);
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

#if NETFRAMEWORK
            return null;
#else
            //
            // We cannot determine the absolute file path from argv[0]
            // (how we were launched), so let's now try to extract the
            // fully resolved executable path from /proc/self/exe.
            // Note that this means we may miss if we've been invoked
            // via a symlink, but it's better than nothing at this point!
            //
            FileSystemInfo fsi = File.ResolveLinkTarget("/proc/self/exe", returnFinalTarget: false);
            return fsi?.FullName;
#endif
        }

        private static string GetMacOSEntryPath()
        {
            // Determine buffer size by passing NULL initially
            LibC._NSGetExecutablePath(IntPtr.Zero, out int size);

            IntPtr bufPtr = Marshal.AllocHGlobal(size);
            int result = LibC._NSGetExecutablePath(bufPtr, out size);

            // buf is null-byte terminated
            string name = result == 0 ? Marshal.PtrToStringAuto(bufPtr) : null;
            Marshal.FreeHGlobal(bufPtr);

            return name;
        }

        private static string GetWindowsEntryPath()
        {
            IntPtr argvPtr = GitCredentialManager.Interop.Windows.Native.Shell32.CommandLineToArgvW(
                GitCredentialManager.Interop.Windows.Native.Kernel32.GetCommandLine(), out _);
            IntPtr argv0Ptr = Marshal.ReadIntPtr(argvPtr);
            string argv0 = Marshal.PtrToStringAuto(argv0Ptr);
            GitCredentialManager.Interop.Windows.Native.Kernel32.LocalFree(argvPtr);

            // If this isn't absolute then we should return null to prevent any
            // caller that expect only an absolute path from mis-using this result.
            // They will have to fall-back to other mechanisms for getting the entry path.
            return Path.IsPathRooted(argv0) ? argv0 : null;
        }

        #endregion

        private static string GetOSType()
        {
            if (IsWindows())
            {
                return "Windows";
            }

            if (IsMacOS())
            {
                return "macOS";
            }

            if (IsLinux())
            {
                return "Linux";
            }

            return "Unknown";
        }
    }
}
