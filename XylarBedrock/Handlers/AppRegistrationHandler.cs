using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XylarBedrock.Handlers
{
    public static class AppRegistrationHandler
    {
        private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XylarBedrock";
        private const string DesktopShortcutName = "XylarBedrock.lnk";
        private const string StartMenuShortcutName = "XylarBedrock.lnk";

        public static void EnsureRegistered()
        {
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return;
            }

            string installDirectory = GetInstallDirectory(exePath);
            TryEnsureUninstallRegistration(exePath, installDirectory);
            EnsureDesktopShortcut(exePath, installDirectory);
        }

        private static void TryEnsureUninstallRegistration(string exePath, string installDirectory)
        {
            try
            {
                using RegistryKey uninstallKey = Registry.CurrentUser.CreateSubKey(UninstallRegistryKey, true);
                if (uninstallKey == null)
                {
                    return;
                }

                uninstallKey.SetValue("DisplayName", App.DisplayName);
                uninstallKey.SetValue("DisplayVersion", App.Version);
                uninstallKey.SetValue("Publisher", "Xylar Inc. and Mrmariix");
                uninstallKey.SetValue("InstallLocation", installDirectory);
                uninstallKey.SetValue("DisplayIcon", exePath);
                uninstallKey.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
                uninstallKey.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
                uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                uninstallKey.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));

                int estimatedSizeKb = GetEstimatedSizeInKilobytes(installDirectory);
                if (estimatedSizeKb > 0)
                {
                    uninstallKey.SetValue("EstimatedSize", estimatedSizeKb, RegistryValueKind.DWord);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to register XylarBedrock in Apps & Features: {ex}");
            }
        }

        public static void RunInteractiveUninstall()
        {
            string installDirectory = GetInstallDirectory();

            DialogResult result = MessageBox.Show(
                "Do you want to remove XylarBedrock from Windows Apps and delete its shortcuts?\n\n" +
                "The launcher files will stay in their current folder, and that folder will open when uninstall finishes.",
                App.DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Unregister();
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopShortcutName));
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), StartMenuShortcutName));

                MessageBox.Show(
                    "XylarBedrock was removed from Windows Apps.\n\n" +
                    "If you do not need it anymore, you can delete this folder manually:\n" +
                    installDirectory,
                    App.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                TryOpenFolder(installDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "XylarBedrock could not finish uninstall cleanly.\n\n" + ex.Message,
                    App.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void Unregister()
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryKey, false);
        }

        private static void DeleteShortcut(string shortcutPath)
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }

        private static string GetInstallDirectory()
        {
            return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string GetInstallDirectory(string exePath)
        {
            string directory = Path.GetDirectoryName(exePath);
            return string.IsNullOrWhiteSpace(directory)
                ? GetInstallDirectory()
                : directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void EnsureDesktopShortcut(string exePath, string installDirectory)
        {
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), DesktopShortcutName);
            object shell = null;
            object shortcut = null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return;
                }

                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { installDirectory });
                shortcutType.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { "XylarBedrock Launcher" });
                shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Unable to create desktop shortcut: {ex}");
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }

                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private static int GetEstimatedSizeInKilobytes(string installDirectory)
        {
            try
            {
                long size = Directory
                    .EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
                    .Select(file => new FileInfo(file).Length)
                    .Sum();

                return (int)Math.Min(int.MaxValue, Math.Max(1, size / 1024));
            }
            catch
            {
                return 0;
            }
        }

        private static void TryOpenFolder(string installDirectory)
        {
            if (!Directory.Exists(installDirectory))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{installDirectory}\"",
                UseShellExecute = true
            });
        }
    }
}
