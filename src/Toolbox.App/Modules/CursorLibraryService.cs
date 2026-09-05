using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Toolbox.Core;

namespace PersonalToolbox.Modules;

public sealed record CursorLibraryLoadResult(
    IReadOnlyList<CursorSkinPackage> Packages,
    string SourcePath,
    bool TargetEstablished,
    bool Migrated,
    bool UsingLegacyFallback,
    string? ErrorMessage);

public sealed class CursorLibraryService
{
    private const string CursorKeyPath = @"Control Panel\Cursors";
    private const string AeroSchemesKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    private const uint SpiSetCursors = 0x0057;
    private const uint SpifUpdateIniFile = 0x0001;
    private const uint SpifSendChange = 0x0002;

    private static readonly string[] CursorKeys =
    [
        "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No",
        "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person",
    ];

    private readonly string _targetLibraryPath;
    private readonly string _legacyLibraryPath;
    private CursorGalleryLibraryLocation? _location;

    public CursorLibraryService()
    {
        _targetLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MU Desk",
            "cursor-gallery",
            "library.json");
        _legacyLibraryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CursorSkinManager",
            "library.json");
    }

    public CursorLibraryLoadResult Load()
    {
        _location = CursorGalleryLibraryMigration.Establish(_targetLibraryPath, _legacyLibraryPath);
        if (!File.Exists(_location.LibraryPath))
        {
            return new CursorLibraryLoadResult(
                [],
                _location.LibraryPath,
                _location.TargetEstablished,
                _location.Migrated,
                _location.UsingLegacyFallback,
                _location.ErrorMessage);
        }

        try
        {
            var packages = JsonSerializer.Deserialize<List<CursorSkinPackage>>(
                File.ReadAllText(_location.LibraryPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            SyncAppliedState(packages);
            return new CursorLibraryLoadResult(
                packages,
                _location.LibraryPath,
                _location.TargetEstablished,
                _location.Migrated,
                _location.UsingLegacyFallback,
                _location.ErrorMessage);
        }
        catch (IOException exception)
        {
            return FailedLoad(exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return FailedLoad(exception);
        }
        catch (JsonException exception)
        {
            return FailedLoad(exception);
        }
    }

    public void Apply(CursorSkinPackage package)
    {
        EnsureWritableTarget();
        var defaults = ReadAeroValues();
        using var cursorKey = Registry.CurrentUser.OpenSubKey(CursorKeyPath, writable: true) ??
            throw new InvalidOperationException("无法打开当前用户的光标设置。");

        foreach (var role in package.Roles)
        {
            var value = role.Exists && !string.IsNullOrWhiteSpace(role.FilePath)
                ? role.FilePath
                : defaults.GetValueOrDefault(role.WindowsKey, string.Empty);
            cursorKey.SetValue(role.WindowsKey, value ?? string.Empty);
        }

        RefreshWindowsCursors();
        UpdateAppliedFlag(package.Id);
    }

    public void ResetToWindowsDefault()
    {
        var defaults = ReadAeroValues();
        using var cursorKey = Registry.CurrentUser.OpenSubKey(CursorKeyPath, writable: true) ??
            throw new InvalidOperationException("无法打开当前用户的光标设置。");

        cursorKey.SetValue(string.Empty, "Windows Aero");
        foreach (var key in CursorKeys)
        {
            cursorKey.SetValue(key, defaults.GetValueOrDefault(key, string.Empty));
        }

        cursorKey.SetValue("Scheme Source", 2, RegistryValueKind.DWord);
        RefreshWindowsCursors();
        if (File.Exists(_targetLibraryPath))
        {
            UpdateAppliedFlag(null);
        }
    }

    private CursorLibraryLoadResult FailedLoad(Exception exception)
    {
        var location = _location ?? new CursorGalleryLibraryLocation(
            _targetLibraryPath, false, false, false, null);
        var prefix = location.UsingLegacyFallback ? "旧光标库读取失败" : "光标库读取失败";
        var migrationMessage = string.IsNullOrWhiteSpace(location.ErrorMessage)
            ? string.Empty
            : location.ErrorMessage + " ";
        return new CursorLibraryLoadResult(
            [],
            location.LibraryPath,
            location.TargetEstablished,
            location.Migrated,
            location.UsingLegacyFallback,
            $"{migrationMessage}{prefix}：{exception.Message}");
    }

    private void EnsureWritableTarget()
    {
        _location ??= CursorGalleryLibraryMigration.Establish(_targetLibraryPath, _legacyLibraryPath);
        if (!_location.TargetEstablished || !File.Exists(_targetLibraryPath))
        {
            throw new InvalidOperationException(
                _location.ErrorMessage ?? "尚未建立 MU Desk 光标库，无法安全保存应用状态。");
        }
    }

    private static void SyncAppliedState(List<CursorSkinPackage> packages)
    {
        using var cursorKey = Registry.CurrentUser.OpenSubKey(CursorKeyPath, writable: false);
        if (cursorKey is null)
        {
            return;
        }

        foreach (var package in packages)
        {
            var assigned = package.Roles.Where(role =>
                role.Exists && !string.IsNullOrWhiteSpace(role.FilePath)).ToArray();
            package.IsApplied = assigned.Length > 0 && assigned.All(role =>
                string.Equals(
                    cursorKey.GetValue(role.WindowsKey) as string,
                    role.FilePath,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Dictionary<string, string> ReadAeroValues()
    {
        using var schemes = Registry.LocalMachine.OpenSubKey(AeroSchemesKeyPath, writable: false);
        var scheme = schemes?.GetValue("Windows Aero") as string;
        if (!string.IsNullOrWhiteSpace(scheme))
        {
            var values = scheme.Split(',').Select(value => value.Trim()).ToArray();
            return CursorKeys
                .Select((key, index) => new { key, value = index < values.Length ? values[index] : string.Empty })
                .ToDictionary(item => item.key, item => item.value, StringComparer.OrdinalIgnoreCase);
        }

        var cursorDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Cursors");
        var files = new[]
        {
            "aero_arrow.cur", "aero_helpsel.cur", "aero_working.ani", "aero_busy.ani", "", "",
            "aero_pen.cur", "aero_unavail.cur", "aero_ns.cur", "aero_ew.cur", "aero_nwse.cur",
            "aero_nesw.cur", "aero_move.cur", "aero_up.cur", "aero_link.cur", "aero_pin.cur", "aero_person.cur",
        };
        return CursorKeys
            .Select((key, index) => new
            {
                key,
                value = files[index].Length == 0 ? string.Empty : Path.Combine(cursorDirectory, files[index]),
            })
            .ToDictionary(item => item.key, item => item.value, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateAppliedFlag(string? appliedId)
    {
        try
        {
            var packages = JsonSerializer.Deserialize<List<CursorSkinPackage>>(
                File.ReadAllText(_targetLibraryPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            foreach (var package in packages)
            {
                package.IsApplied = string.Equals(package.Id, appliedId, StringComparison.Ordinal);
            }

            var directory = Path.GetDirectoryName(_targetLibraryPath)
                ?? throw new InvalidOperationException("MU Desk 光标库路径无效。");
            var backupPath = Path.Combine(directory, "library.bak.json");
            var temporaryPath = Path.Combine(directory, $"library.{Guid.NewGuid():N}.tmp");
            File.Copy(_targetLibraryPath, backupPath, overwrite: true);
            try
            {
                File.WriteAllText(
                    temporaryPath,
                    JsonSerializer.Serialize(packages, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true,
                    }));
                File.Move(temporaryPath, _targetLibraryPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"光标已切换，但 MU Desk 无法保存当前皮肤状态：{exception.Message}",
                exception);
        }
    }

    private static void RefreshWindowsCursors()
    {
        if (!SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows 无法立即刷新光标。");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);
}
