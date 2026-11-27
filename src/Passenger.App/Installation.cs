using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack;

namespace Passenger.App;

/// <summary>Passenger as its Windows installer left it (tools/package-win.sh): Velopack's, per user, in
/// <c>%LocalAppData%\PitstopIDE\current</c> beside its manifest (<c>sq.version</c>), Update.exe above. Its installer,
/// updater and uninstaller run Pitstop.exe with a hook's flag before anything else of it, and Passenger answers by
/// registering or removing the file types it opens and the pitstop-ide command's folder on the person's own Path
/// (<see cref="PassengerCommand"/>), then exits. A copy anywhere else (a build, the tests, macOS) never
/// starts Velopack, which would only add to a log of its own in local app data at every start.</summary>
internal static partial class Installation
{
    // The same ID is written into the Windows shortcut by tools/package-win.sh.
    private const string AppUserModelId = "velopack.PitstopIDE";

    /// <summary>C, C++ and Lua files offered through the Windows Open With menu.</summary>
    private static readonly (string Extension, string ProgId, string Name)[] FileTypes =
    [
        (".c", "Pitstop.c", "C Source File"), (".h", "Pitstop.h", "C or C++ Header"),
        (".cpp", "Pitstop.cpp", "C++ Source File"), (".cc", "Pitstop.cc", "C++ Source File"), (".cxx", "Pitstop.cxx", "C++ Source File"),
        (".hpp", "Pitstop.hpp", "C++ Header"), (".hh", "Pitstop.hh", "C++ Header"), (".hxx", "Pitstop.hxx", "C++ Header"),
        (".cppm", "Pitstop.cppm", "C++ Module Source File"), (".ixx", "Pitstop.ixx", "C++ Module Source File"),
        (".mpp", "Pitstop.mpp", "C++ Module Source File"), (".cxxm", "Pitstop.cxxm", "C++ Module Source File"), (".ccm", "Pitstop.ccm", "C++ Module Source File"),
        (".lua", "Pitstop.lua", "Lua Source File"),
    ];

    private static readonly (string Extension, string ProgId, string Name)[] LegacyFileTypes =
    [
        (".sln", "Pitstop.sln", "Solution File"),
        (".slnx", "Pitstop.slnx", "Solution File"),
        (".slnf", "Pitstop.slnf", "Solution Filter File"),
        (".csproj", "Pitstop.csproj", "C# Project File"),
    ];

    private const string Classes = @"Software\Classes";

    /// <summary>Whether Velopack's Update.exe runs this copy as the version an update replaces (its obsolete hook, which
    /// Passenger answers with nothing): Update.exe renames current\ the moment this process has gone, and is refused for as
    /// long as anything of it holds the folder, so it is answered before anything else of Passenger loads, out of current\
    /// at once.</summary>
    public static bool Obsolete(string[] args) => args is [ObsoleteHook, ..] or [LegacyObsoleteHook, ..];

    /// <summary>The obsolete hook's flags, Velopack's and Squirrel's before it, which VelopackApp answers alike.</summary>
    private const string ObsoleteHook = "--veloapp-obsolete";
    private const string LegacyObsoleteHook = "--squirrel-obsolete";

    /// <summary>Whether this copy is one Velopack packed, whose start Main hands to Velopack first.</summary>
    public static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "sq.version"));

    public static bool IsInstallerHook(string[] args) => OperatingSystem.IsWindows() && Installed && args is
        ["--veloapp-install" or "--veloapp-updated" or "--veloapp-obsolete" or "--veloapp-uninstall", _];

    public static void RegisterProcessIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
    }

    /// <summary>Velopack's start with Passenger's hooks, which are Windows' alone.</summary>
    public static VelopackApp Hooked(VelopackApp app) => OperatingSystem.IsWindows() ? WithHooks(app) : app;

    /// <summary>An update runs the new version's hook too: what it opens may have changed, and current\, the path the
    /// types and the Path name, stays where it was; an install updated from a release before the command takes it then.</summary>
    [SupportedOSPlatform("windows")]
    private static VelopackApp WithHooks(VelopackApp app) => app
        .OnAfterInstallFastCallback(_ => TakeInstall())
        .OnAfterUpdateFastCallback(_ => TakeInstall())
        .OnBeforeUninstallFastCallback(_ => TakeUninstall());

    /// <summary>The file types and the command's folder, each made whatever the other came to, a failure failing the hook.</summary>
    [SupportedOSPlatform("windows")]
    private static void TakeInstall()
    {
        try
        {
            UnregisterFileTypes(LegacyFileTypes);
            RegisterFileTypes();
        }
        finally
        {
            PassengerCommand.PutOnPath();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TakeUninstall()
    {
        try
        {
            PassengerCommand.TakeOffPath();
        }
        finally
        {
            UnregisterFileTypes();
        }
    }

    /// <summary>Registers Pitstop for Open With under HKEY_CURRENT_USER, leaving existing defaults to Windows.</summary>
    [SupportedOSPlatform("windows")]
    private static void RegisterFileTypes()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Pitstop.exe has no path");
        using RegistryKey classes = Registry.CurrentUser.CreateSubKey(Classes);
        foreach ((string extension, string progId, string name) in FileTypes)
        {
            using (RegistryKey type = classes.CreateSubKey(progId))
            {
                type.SetValue(string.Empty, name);
                using RegistryKey icon = type.CreateSubKey("DefaultIcon");
                icon.SetValue(string.Empty, $"\"{exe}\",0");
                using RegistryKey command = type.CreateSubKey(@"shell\open\command");
                command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
            }
            using RegistryKey ext = classes.CreateSubKey(extension);
            using (RegistryKey openWith = ext.CreateSubKey("OpenWithProgids"))
            {
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        AssociationsChanged();
    }

    /// <summary>What <see cref="RegisterFileTypes"/> wrote, and a type's key when nothing else is left in it.</summary>
    [SupportedOSPlatform("windows")]
    private static void UnregisterFileTypes() => UnregisterFileTypes(FileTypes.Concat(LegacyFileTypes));

    [SupportedOSPlatform("windows")]
    private static void UnregisterFileTypes(IEnumerable<(string Extension, string ProgId, string Name)> types)
    {
        using RegistryKey classes = Registry.CurrentUser.CreateSubKey(Classes);
        foreach ((string extension, string progId, string _) in types)
        {
            classes.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);
            using (RegistryKey? ext = classes.OpenSubKey(extension, writable: true))
            {
                if (ext is null)
                {
                    continue;
                }
                using (RegistryKey? openWith = ext.OpenSubKey("OpenWithProgids", writable: true))
                {
                    openWith?.DeleteValue(progId, throwOnMissingValue: false);
                }
                if (ext.GetValue(string.Empty) as string == progId)
                {
                    ext.DeleteValue(string.Empty, throwOnMissingValue: false);
                }
                DeleteIfEmpty(ext, "OpenWithProgids");
            }
            DeleteIfEmpty(classes, extension);
        }
        AssociationsChanged();
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteIfEmpty(RegistryKey parent, string name)
    {
        bool empty;
        using (RegistryKey? key = parent.OpenSubKey(name))
        {
            empty = key is { ValueCount: 0, SubKeyCount: 0 };
        }
        if (empty)
        {
            parent.DeleteSubKey(name, throwOnMissingSubKey: false);
        }
    }

    /// <summary>Tells Explorer the types changed, so their icons and Open with change without a sign-out.</summary>
    private static void AssociationsChanged() => SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    private const int ShcneAssocChanged = 0x08000000;

    private const uint ShcnfIdList = 0;

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
