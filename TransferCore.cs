using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace EverQuestBetaUiCopier
{
    public sealed class Profile
    {
        public string Character { get; set; }
        public string Server { get; set; }
        public string ClassCode { get; set; }
        public string UiSourcePath { get; set; }
        public string CompanionSourcePath { get; set; }
        public string DestinationUiName { get; set; }
        public string DestinationCompanionName { get; set; }
    }

    public sealed class TransferResult
    {
        public int CopiedFiles { get; internal set; }
        public int ReplacedFiles { get; internal set; }
        public string BackupDirectory { get; internal set; }
    }

    public static class TransferCore
    {
        private static readonly Regex UiName = new Regex(
            @"\AUI_(?<character>[a-z]+)_(?<server>[a-z0-9]+)_(?<class>WAR|CLR|PAL|RNG|SHD|DRU|MNK|BRD|ROG|SHM|NEC|WIZ|MAG|ENC|BST|BER)\.ini\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

        public static List<Profile> Scan(string liveDirectory)
        {
            string root = ExistingDirectory(liveDirectory, "Live");
            var files = new Dictionary<string, string>(Names);
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                if (files.ContainsKey(name))
                    throw new IOException("Live contains filenames that differ only by letter case: " + name);
                files.Add(name, path);
            }

            var profiles = new List<Profile>();
            foreach (var entry in files)
            {
                Match match = UiName.Match(entry.Key);
                if (!match.Success || Names.Equals(match.Groups["server"].Value, "beta"))
                    continue;
                string companion;
                files.TryGetValue(entry.Key.Substring(3), out companion);
                string destination = match.Groups["character"].Value + "_beta_" + match.Groups["class"].Value + ".ini";
                profiles.Add(new Profile
                {
                    Character = match.Groups["character"].Value,
                    Server = match.Groups["server"].Value,
                    ClassCode = match.Groups["class"].Value,
                    UiSourcePath = entry.Value,
                    CompanionSourcePath = companion,
                    DestinationUiName = "UI_" + destination,
                    DestinationCompanionName = destination
                });
            }
            return profiles.OrderBy(p => p.Character, Names).ThenBy(p => p.ClassCode, Names)
                .ThenBy(p => p.Server, Names).ToList();
        }

        public static void ValidateFolders(string liveDirectory, string betaDirectory)
        {
            string live = ExistingDirectory(liveDirectory, "Live");
            string beta = ExistingDirectory(betaDirectory, "Beta");
            if (Names.Equals(CanonicalDirectory(live), CanonicalDirectory(beta)))
                throw new InvalidOperationException("Live and Beta must be different EverQuest folders.");
            RequireGame(live, "Live");
            RequireGame(beta, "Beta");
        }

        public static TransferResult Copy(string liveDirectory, string betaDirectory, IEnumerable<Profile> selectedProfiles)
        {
            ValidateFolders(liveDirectory, betaDirectory);
            string live = Path.GetFullPath(liveDirectory);
            string beta = Path.GetFullPath(betaDirectory);
            if (selectedProfiles == null)
                throw new ArgumentException("Select at least one character profile to copy.");

            // Always derive paths from a fresh scan, never from editable UI objects.
            var current = Scan(live).ToDictionary(p => p.UiSourcePath, Names);
            var selected = selectedProfiles.ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("Select at least one character profile to copy.");
            var plan = new List<CopyItem>();
            var destinations = new HashSet<string>(Names);
            foreach (Profile requested in selected)
            {
                Profile profile;
                if (requested == null || String.IsNullOrWhiteSpace(requested.UiSourcePath) ||
                    !current.TryGetValue(Path.GetFullPath(requested.UiSourcePath), out profile))
                    throw new InvalidOperationException("A selected Live profile changed or disappeared. Choose the Live launcher again to refresh the list.");
                AddItem(plan, destinations, beta, profile.UiSourcePath, profile.DestinationUiName);
                if (profile.CompanionSourcePath != null)
                    AddItem(plan, destinations, beta, profile.CompanionSourcePath, profile.DestinationCompanionName);
            }

            // Reject unsafe targets before creating a backup directory or writing any data.
            foreach (CopyItem item in plan)
            {
                RequireRegularFile(item.Source, "Live source");
                ValidateDestination(item.Target);
            }
            string backupsRoot = Path.Combine(beta, "BetaUI-Backups");
            if (File.Exists(backupsRoot))
                throw new IOException("The backup location is occupied by a file: " + backupsRoot);
            if (Directory.Exists(backupsRoot))
                RejectReparsePoint(backupsRoot, "backup folder");
            Directory.CreateDirectory(backupsRoot);
            RejectReparsePoint(backupsRoot, "backup folder");
            string run = Path.Combine(backupsRoot,
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(run);
            string staging = Path.Combine(run, "staged");
            string originals = Path.Combine(run, "originals");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(originals);
            string manifest = Path.Combine(run, "transfer.txt");
            var attempted = new List<CopyItem>();
            try
            {
                // All source reads and backup copies must succeed before changing a Beta file.
                foreach (CopyItem item in plan)
                {
                    item.Staged = Path.Combine(staging, Path.GetFileName(item.Target));
                    File.Copy(item.Source, item.Staged, false);
                    File.SetAttributes(item.Staged, FileAttributes.Normal);
                    ValidateDestination(item.Target);
                    item.Existed = File.Exists(item.Target);
                    if (item.Existed)
                    {
                        item.OriginalAttributes = File.GetAttributes(item.Target);
                        item.OriginalWriteTimeUtc = File.GetLastWriteTimeUtc(item.Target);
                        item.Backup = Path.Combine(originals, Path.GetFileName(item.Target));
                        File.Copy(item.Target, item.Backup, false);
                    }
                }
                WriteManifest(manifest, "PREPARED: all source files staged and existing Beta files backed up.", live, beta, plan);
                foreach (CopyItem item in plan)
                {
                    ValidateDestination(item.Target);
                    attempted.Add(item); // A failed overwrite may still have modified the target.
                    File.Copy(item.Staged, item.Target, true);
                }
                WriteManifest(manifest, "SUCCESS: transfer completed.", live, beta, plan);
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<string>();
                for (int i = attempted.Count - 1; i >= 0; --i)
                {
                    CopyItem item = attempted[i];
                    try
                    {
                        ValidateDestination(item.Target);
                        if (item.Existed)
                        {
                            // A read-only target rejects the initial overwrite without changing bytes.
                            if (File.Exists(item.Target) && SameContents(item.Target, item.Backup))
                                continue;
                            File.Copy(item.Backup, item.Target, true);
                            File.SetLastWriteTimeUtc(item.Target, item.OriginalWriteTimeUtc);
                            File.SetAttributes(item.Target, item.OriginalAttributes);
                        }
                        else if (File.Exists(item.Target))
                            File.Delete(item.Target);
                    }
                    catch (Exception rollbackFailure)
                    {
                        rollbackErrors.Add(Path.GetFileName(item.Target) + ": " + rollbackFailure.Message);
                    }
                }
                string state = rollbackErrors.Count == 0
                    ? "FAILED: transfer did not complete. All attempted Beta changes were rolled back."
                    : "FAILED: rollback was incomplete. Restore the listed files from the originals folder before playing.";
                string details = state + Environment.NewLine + failure.Message;
                if (rollbackErrors.Count > 0)
                    details += Environment.NewLine + "Restore problems:" + Environment.NewLine + String.Join(Environment.NewLine, rollbackErrors);
                try { WriteManifest(manifest, details, live, beta, plan); }
                catch (Exception logFailure) { details += Environment.NewLine + "Could not update the transfer log: " + logFailure.Message; }
                throw new IOException(details + Environment.NewLine + "Backups and transfer log: " + run, failure);
            }
            finally
            {
                // Cleanup is deliberately limited to the staging directory created for this run.
                try { Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            return new TransferResult
            {
                CopiedFiles = plan.Count,
                ReplacedFiles = plan.Count(p => p.Existed),
                BackupDirectory = run
            };
        }

        private static void AddItem(List<CopyItem> plan, HashSet<string> destinations, string beta, string source, string destinationName)
        {
            if (!destinations.Add(destinationName))
                throw new InvalidOperationException("More than one selected profile would become " + destinationName + ". Select only one Live server for that character and class.");
            plan.Add(new CopyItem { Source = source, Target = Path.Combine(beta, destinationName) });
        }

        private static string ExistingDirectory(string path, string label)
        {
            if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                throw new DirectoryNotFoundException("The " + label + " EverQuest folder could not be found.");
            return Path.GetFullPath(path);
        }

        private static void RequireGame(string directory, string label)
        {
            if (!File.Exists(Path.Combine(directory, "eqgame.exe")))
                throw new InvalidOperationException("The " + label + " folder does not contain eqgame.exe. Select that installation's EverQuest launcher.");
        }

        private static void RequireRegularFile(string path, string label)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(label + " file could not be found: " + path, path);
            RejectReparsePoint(path, label + " file");
        }

        private static void ValidateDestination(string path)
        {
            if (Directory.Exists(path))
                throw new IOException("A folder occupies the Beta filename: " + path);
            // GetAttributes also detects dangling reparse points that File.Exists may not see.
            try
            {
                RejectReparsePoint(path, "Beta destination file");
                RejectHardLinkedDestination(path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static void RejectHardLinkedDestination(string path)
        {
            // Hard links do not have the ReparsePoint attribute. Overwriting one could
            // also modify a Live source or another file outside the selected Beta folder.
            using (SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 2 || error == 3)
                        throw new FileNotFoundException("The Beta destination disappeared while it was being checked: " + path, path);
                    throw new IOException("Could not safely inspect the Beta destination: " + path, new System.ComponentModel.Win32Exception(error));
                }
                FileInformation information;
                if (!GetFileInformationByHandle(handle, out information))
                    throw new IOException("Could not verify the link count of the Beta destination: " + path, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                if (information.NumberOfLinks > 1)
                    throw new IOException("The Beta destination has multiple hard links and cannot be overwritten safely: " + path + ". Replace it with an independent file before copying.");
            }
        }

        private static void RejectReparsePoint(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The " + label + " is a link or reparse point and cannot be used safely: " + path);
        }

        private static bool SameContents(string first, string second)
        {
            using (var a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (a.Length != b.Length) return false;
                var ab = new byte[8192];
                var bb = new byte[8192];
                int count;
                while ((count = a.Read(ab, 0, ab.Length)) != 0)
                {
                    int offset = 0;
                    while (offset < count)
                    {
                        int read = b.Read(bb, offset, count - offset);
                        if (read == 0) return false;
                        offset += read;
                    }
                    for (int i = 0; i < count; i++)
                        if (ab[i] != bb[i]) return false;
                }
                return true;
            }
        }

        private static void WriteManifest(string path, string state, string live, string beta, List<CopyItem> plan)
        {
            var content = new StringBuilder();
            content.AppendLine("EverQuest Beta UI Copier");
            content.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
            content.AppendLine(state);
            content.AppendLine();
            content.AppendLine("Live: " + live);
            content.AppendLine("Beta: " + beta);
            content.AppendLine("INI contents are copied unchanged; only the server segment of each filename becomes beta.");
            content.AppendLine();
            foreach (CopyItem item in plan)
                content.AppendLine(Path.GetFileName(item.Source) + " -> " + Path.GetFileName(item.Target) +
                    (item.Backup != null ? " | existing Beta file backed up in originals" : " | new Beta file"));
            content.AppendLine();
            content.AppendLine("To restore after a successful transfer: close Beta, copy files from originals into the Beta folder, and remove any listed new Beta files if you want to undo those additions.");
            File.WriteAllText(path, content.ToString(), new UTF8Encoding(true));
        }

        private static string CanonicalDirectory(string path)
        {
            using (SafeFileHandle handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    throw new IOException("Could not verify the EverQuest folder: " + path, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                var buffer = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length == 0 || length >= buffer.Capacity)
                    throw new IOException("Could not resolve the EverQuest folder: " + path, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
                return buffer.ToString().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private sealed class CopyItem
        {
            internal string Source;
            internal string Target;
            internal string Staged;
            internal string Backup;
            internal bool Existed;
            internal FileAttributes OriginalAttributes;
            internal DateTime OriginalWriteTimeUtc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            internal uint VolumeSerialNumber;
            internal uint FileSizeHigh;
            internal uint FileSizeLow;
            internal uint NumberOfLinks;
            internal uint FileIndexHigh;
            internal uint FileIndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    }
}
