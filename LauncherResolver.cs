using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EverQuestBetaUiCopier
{
    public static class LauncherResolver
    {
        public static string GetGameDirectory(string selectedPath)
        {
            string path = Path.GetFullPath(selectedPath);
            if (!File.Exists(path))
                throw new IOException("That launcher or shortcut no longer exists. Please choose it again.");

            // Read shortcut metadata only. Never execute the launcher or its arguments.
            if (String.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                object shell = null;
                object shortcut = null;
                try
                {
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
                    shell = Activator.CreateInstance(shellType);
                    shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                        null, shell, new object[] { path });
                    path = (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
                        null, shortcut, null);
                }
                finally
                {
                    if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                    if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
                }
                if (String.IsNullOrWhiteSpace(path))
                    throw new IOException("This shortcut has no file target. Choose LaunchPad.exe in the game folder instead.");
                path = Environment.ExpandEnvironmentVariables(path);
            }

            string fileName = Path.GetFileName(path);
            if (!String.Equals(fileName, "LaunchPad.exe", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(fileName, "eqgame.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose an EverQuest shortcut, LaunchPad.exe, or eqgame.exe.");
            if (!File.Exists(path))
                throw new IOException("The shortcut points to a missing launcher. Choose LaunchPad.exe in the installed game folder.");

            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!File.Exists(Path.Combine(folder, "eqgame.exe")))
                throw new IOException("This folder does not contain eqgame.exe. Finish installing EverQuest, then choose its launcher.");
            return folder;
        }
    }
}
