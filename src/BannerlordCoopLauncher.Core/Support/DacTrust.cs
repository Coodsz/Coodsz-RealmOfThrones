using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace BannerlordCoopLauncher.Core.Support;

/// <summary>
/// Which DAC (the .NET debugging DLL ClrMD loads to read managed frames) DumpStackSummary may load. The
/// dump names its own runtime folder, so a hostile dump could point at a DLL of its own: a DAC only loads
/// from .NET Framework's Windows folder, the installed .NET in Program Files or a server folder the tool
/// already runs (engine\dotnet), and only with a valid Authenticode signature (in the file or a Windows
/// catalog) by Microsoft that chains to a Microsoft root.
/// </summary>
internal static class DacTrust
{
    private static readonly string[] DacNames = ["mscordacwks.dll", "mscordaccore.dll"];

    // Microsoft Root Certificate Authority 2010 and 2011.
    private static readonly string[] MicrosoftRoots = ["3B1EFD3A66EA28B16697394703A72CA340A05BD5", "8F43288AD272F3103B6FB1428485EA3014C0BCFE"];

    /// <summary>Why the DAC at path must not load, or null. serverFolders are the server folders the tool runs.</summary>
    public static string? Refusal(string path, IReadOnlyList<string> serverFolders) =>
        FolderRefusal(path, serverFolders) ?? SignatureRefusal(path) ?? NameRefusal(path);

    /// <summary>
    /// True only for a drive letter path (C:\...) on a fixed drive of this PC. A path that came from a dump
    /// passes this before any File.Exists or open: a network path (\\host\share, \\?\UNC\, a mapped
    /// drive) or a device path (\\?\, \\.\) would make Windows reach out to that host. Pure string checks,
    /// then the drive type of the letter, which never touches the network.
    /// </summary>
    public static bool IsLocalDrivePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            return false;
        // A second colon is a file stream or a device name; a nul ends the path early.
        if (path.IndexOf(':', 2) >= 0 || path.Contains('\0'))
            return false;
        try
        {
            return new DriveInfo(path[..1]).DriveType == DriveType.Fixed;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A module name from a dump that ClrMD may look up on disk: a bare file name, or a local drive path.</summary>
    public static bool IsSafeModuleName(string? name) =>
        string.IsNullOrEmpty(name) || name.IndexOfAny(['\\', '/', ':']) < 0 || IsLocalDrivePath(name);

    /// <summary>Why path is not in a folder a DAC may load from, or null. Checked before the file is touched.</summary>
    public static string? FolderRefusal(string path, IReadOnlyList<string> serverFolders)
    {
        if (!IsLocalDrivePath(path))
            return "the DAC is not on a drive of this PC";
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return "the DAC path is not a valid path";
        }

        if (!DacNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
            return "the file is not a DAC";
        var folder = Path.GetDirectoryName(full) ?? "";
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && new[] { "Framework64", "Framework" }.Any(framework => Same(folder, Path.Combine(windows, "Microsoft.NET", framework, "v4.0.30319"))))
            return null;
        // .NET keeps each version in its own folder: shared\Microsoft.NETCore.App\<version>\mscordaccore.dll.
        var versions = Path.GetDirectoryName(folder) ?? "";
        var roots = serverFolders.Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => Path.Combine(server, "engine", "dotnet"))
            .Prepend(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        if (roots.Any(root => Same(versions, Path.Combine(root, "shared", "Microsoft.NETCore.App"))))
            return null;
        return "the DAC is not in a .NET folder of Windows, Program Files or the server";
    }

    private static bool Same(string a, string b)
    {
        try
        {
            return Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the DAC so it cannot be changed or replaced until the handle closes (it still loads, which
    /// only reads), then checks its signature. Null with the reason when it must not load.
    /// </summary>
    public static FileStream? OpenTrusted(string path, IReadOnlyList<string> serverFolders, out string? refusal)
    {
        refusal = FolderRefusal(path, serverFolders);
        if (refusal != null)
            return null;
        FileStream hold;
        try
        {
            hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusal = "the DAC could not be opened";
            return null;
        }

        refusal = SignatureRefusal(path, hold.SafeFileHandle) ?? NameRefusal(path);
        if (refusal == null)
            return hold;
        hold.Dispose();
        return null;
    }

    /// <summary>Why the file has no valid Microsoft signature, or null.</summary>
    public static string? SignatureRefusal(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return SignatureRefusal(path, file.SafeFileHandle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "the DAC could not be opened";
        }
    }

    private static string? SignatureRefusal(string path, SafeFileHandle file)
    {
        if (!OperatingSystem.IsWindows())
            return "signatures are only checked on Windows";
        try
        {
            // A file of Windows itself (like .NET Framework's DAC) is signed in a catalog, not in the file.
            var catalog = Catalog(file, out var tag);
            using var signer = Verify(path, file, catalog, tag, out var root, out var result);
            if (result != 0)
                return "the DAC's signature is not valid (0x" + result.ToString("X8") + ")";
            if (signer == null || !IsDacSigner(signer))
                return "the DAC is not signed by Microsoft";
            if (root == null || !MicrosoftRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
                return "the DAC's signature does not lead to a Microsoft root";
            return null;
        }
        catch (Exception ex)
        {
            return "the DAC's signature could not be checked (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// True for the signers of real DACs: .NET's own (".NET DAC"), Windows' catalogs ("Microsoft Windows")
    /// or "Microsoft Corporation", all of organisation Microsoft Corporation. Other Microsoft signers, like
    /// the one that signs third party drivers in WHQL catalogs, are not.
    /// </summary>
    internal static bool IsDacSigner(X509Certificate2 signer)
    {
        string? common = null, organisation = null;
        foreach (var part in signer.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            var oid = part.GetSingleElementType().Value;
            if (oid == "2.5.4.3")
                common = part.GetSingleElementValue();
            else if (oid == "2.5.4.10")
                organisation = part.GetSingleElementValue();
        }

        return organisation == "Microsoft Corporation" && DacSigners.Contains(common, StringComparer.Ordinal);
    }

    private static readonly string[] DacSigners = [".NET DAC", "Microsoft Windows", "Microsoft Corporation"];

    /// <summary>Why the file's version resource does not name it as this DAC (OriginalFilename), or null.</summary>
    public static string? NameRefusal(string path)
    {
        try
        {
            var original = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).OriginalFilename?.Trim();
            return original != null && DacNames.Contains(original, StringComparer.OrdinalIgnoreCase)
                                    && original.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
                ? null
                : "the file's version information does not name it a DAC";
        }
        catch (Exception)
        {
            return "the file's version information could not be read";
        }
    }

    // The catalog file that signs this file and the file's hash as its member tag, or null.
    private static string? Catalog(SafeFileHandle file, out string? tag)
    {
        tag = null;
        foreach (var algorithm in new[] { "SHA256", null })
        {
            if (!CryptCATAdminAcquireContext2(out var admin, IntPtr.Zero, algorithm, IntPtr.Zero, 0))
                continue;
            try
            {
                uint size = 0;
                CryptCATAdminCalcHashFromFileHandle2(admin, file.DangerousGetHandle(), ref size, null, 0);
                if (size == 0)
                    continue;
                var hash = new byte[size];
                if (!CryptCATAdminCalcHashFromFileHandle2(admin, file.DangerousGetHandle(), ref size, hash, 0))
                    continue;
                var info = CryptCATAdminEnumCatalogFromHash(admin, hash, size, 0, IntPtr.Zero);
                if (info == IntPtr.Zero)
                    continue;
                try
                {
                    var catalog = new CatalogInfo { Size = (uint)Marshal.SizeOf<CatalogInfo>() };
                    if (!CryptCATCatalogInfoFromContext(info, ref catalog, 0))
                        continue;
                    tag = Convert.ToHexString(hash);
                    return catalog.File;
                }
                finally
                {
                    CryptCATAdminReleaseCatalogContext(admin, info, 0);
                }
            }
            finally
            {
                CryptCATAdminReleaseContext(admin, 0);
            }
        }

        return null;
    }

    // WinVerifyTrust on the file or its catalog member, offline (no revocation lookups). The signer and
    // the thumbprint of the root its chain ends in come from the verified state.
    private static X509Certificate2? Verify(string path, SafeFileHandle file, string? catalog, string? tag, out string? root, out int result)
    {
        root = null;
        var strings = new List<IntPtr>();
        IntPtr Text(string value)
        {
            var pointer = Marshal.StringToHGlobalUni(value);
            strings.Add(pointer);
            return pointer;
        }

        IntPtr info;
        uint choice;
        if (catalog != null && tag != null)
        {
            var member = new CatalogData
            {
                Size = (uint)Marshal.SizeOf<CatalogData>(),
                CatalogFilePath = Text(catalog),
                MemberTag = Text(tag),
                MemberFilePath = Text(path),
                MemberFile = file.DangerousGetHandle()
            };
            info = Marshal.AllocHGlobal(Marshal.SizeOf<CatalogData>());
            Marshal.StructureToPtr(member, info, false);
            choice = 2; // WTD_CHOICE_CATALOG
        }
        else
        {
            var data = new FileData { Size = (uint)Marshal.SizeOf<FileData>(), FilePath = Text(path), File = file.DangerousGetHandle() };
            info = Marshal.AllocHGlobal(Marshal.SizeOf<FileData>());
            Marshal.StructureToPtr(data, info, false);
            choice = 1; // WTD_CHOICE_FILE
        }

        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var trust = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = 2, // WTD_UI_NONE
            RevocationChecks = 0, // WTD_REVOKE_NONE
            UnionChoice = choice,
            Info = info,
            StateAction = 1, // WTD_STATEACTION_VERIFY
            ProviderFlags = 0x10 | 0x1000 // WTD_REVOCATION_CHECK_NONE, WTD_CACHE_ONLY_URL_RETRIEVAL
        };
        try
        {
            result = WinVerifyTrust(new IntPtr(-1), ref action, ref trust);
            if (result != 0)
                return null;
            var provider = WTHelperProvDataFromStateData(trust.StateData);
            var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == IntPtr.Zero)
                return null;
            // CRYPT_PROVIDER_SGNR: cbStruct, then a FILETIME, then the chain's length.
            var chainLength = Marshal.ReadInt32(signer, 12);
            X509Certificate2? Cert(uint index)
            {
                var cert = WTHelperGetProvCertFromChain(signer, index);
                // CRYPT_PROVIDER_CERT: cbStruct, then the certificate context pointer.
                var context = cert == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(cert, IntPtr.Size);
                return context == IntPtr.Zero ? null : new X509Certificate2(context);
            }

            if (chainLength > 0)
                using (var last = Cert((uint)chainLength - 1))
                    root = last?.Thumbprint;
            return Cert(0);
        }
        finally
        {
            trust.StateAction = 2; // WTD_STATEACTION_CLOSE
            WinVerifyTrust(new IntPtr(-1), ref action, ref trust);
            Marshal.FreeHGlobal(info);
            foreach (var pointer in strings)
                Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileData
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CatalogData
    {
        public uint Size;
        public uint CatalogVersion;
        public IntPtr CatalogFilePath;
        public IntPtr MemberTag;
        public IntPtr MemberFilePath;
        public IntPtr MemberFile;
        public IntPtr CalculatedFileHash;
        public uint CalculatedFileHashSize;
        public IntPtr CatalogContext;
        public IntPtr CatalogAdmin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr Info;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string File;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [DllImport("wintrust.dll")]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);

    [DllImport("wintrust.dll")]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    [DllImport("wintrust.dll")]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint cert);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string? hashAlgorithm, IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, IntPtr file, ref uint hashSize, byte[]? hash, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, uint hashSize, uint flags, IntPtr previous);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalogInfo, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catalogInfo, uint flags);

    [DllImport("wintrust.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);
}
