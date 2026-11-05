using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;

namespace EverQuestBetaUiCopier
{
    public sealed class MapFile
    {
        public string Name { get; private set; }
        public byte[] Contents { get; private set; }

        public MapFile(string name, byte[] contents)
        {
            ValidateName(name);
            if (contents == null) throw new ArgumentNullException("contents");
            Name = name;
            Contents = contents;
        }

        public static void ValidateName(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 150 ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 ||
                name.EndsWith(" ", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal) ||
                !String.Equals(Path.GetExtension(name), ".txt", StringComparison.OrdinalIgnoreCase) ||
                String.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(name)))
                throw new InvalidDataException("The map download contains an invalid map filename: " + name);

            // Windows reserves device names even when they have a file extension.
            string device = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) ||
                    device.StartsWith("LPT", StringComparison.Ordinal)) && "123456789\u00b9\u00b2\u00b3".IndexOf(device[3]) >= 0))
                throw new InvalidDataException("The map download contains a reserved Windows filename: " + name);
        }
    }

    public static class MapDownloader
    {
        public const string ArchiveUrl = "https://codeload.github.com/Ewiclip/Beta-UI-Copier/zip/refs/heads/main";
        internal const int MaxArchiveBytes = 64 * 1024 * 1024;
        internal const int MaxMapBytes = 16 * 1024 * 1024;
        internal const int MaxTotalMapBytes = 128 * 1024 * 1024;
        internal const int MaxMapFiles = 4096;
        private const int MaxArchiveEntries = 25000;

        public static List<MapFile> Download(Action<string> progress)
        {
            try
            {
                if (progress != null) progress("Downloading new maps from GitHub...");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ArchiveUrl);
                request.UserAgent = "EverQuest-Beta-UI-Copier/1.1";
                request.Timeout = 45000;
                request.ReadWriteTimeout = 45000;
                request.AllowAutoRedirect = false;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw new IOException("GitHub returned HTTP " + (int)response.StatusCode + ".");
                    if (response.ContentLength > MaxArchiveBytes)
                        throw new InvalidDataException("The map archive is larger than the supported download limit.");
                    using (Stream source = response.GetResponseStream())
                    using (MemoryStream archive = ReadBounded(source, MaxArchiveBytes, "map archive", Stopwatch.StartNew()))
                    {
                        if (progress != null) progress("Checking downloaded maps...");
                        List<MapFile> maps = ReadArchive(archive);
                        if (progress != null) progress("Downloaded " + maps.Count + " maps.");
                        return maps;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is WebException) && !(ex is IOException) && !(ex is InvalidDataException) && !(ex is NotSupportedException))
                    throw;
                throw new IOException("Could not download the new maps from GitHub. No game files have been changed. " +
                    "Check your connection and try again, or uncheck Include New Maps to copy only UI files.\n\n" + ex.Message, ex);
            }
        }

        // Parse in memory: ZIP entry paths are never extracted to the filesystem.
        // This method also supports offline archive fixtures without making a network request.
        public static List<MapFile> ReadArchive(Stream source)
        {
            if (source == null) throw new ArgumentNullException("source");
            using (MemoryStream bounded = ReadBounded(source, MaxArchiveBytes, "map archive", null))
            using (ZipArchive archive = new ZipArchive(bounded, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count > MaxArchiveEntries)
                    throw new InvalidDataException("The map archive contains too many entries.");
                List<MapFile> result = new List<MapFile>();
                HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string root = null;
                long totalBytes = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string path = entry.FullName;
                    string[] parts = path.Split('/');
                    if (path.IndexOf('\\') >= 0 || parts.Length < 2 ||
                        parts[0].Length == 0 || parts.Any(p => p == "." || p == ".." || p.IndexOf(':') >= 0) ||
                        parts.Take(parts.Length - 1).Any(p => p.Length == 0))
                        throw new InvalidDataException("The map archive contains an invalid entry path.");
                    if (root == null) root = parts[0];
                    if (!String.Equals(root, parts[0], StringComparison.Ordinal))
                        throw new InvalidDataException("The map archive contains more than one repository root.");

                    // Only top-level text files in the repository's maps folder are installed.
                    if (parts.Length != 3 || !String.Equals(parts[1], "maps", StringComparison.Ordinal) ||
                        !parts[2].EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string name = parts[2];
                    MapFile.ValidateName(name);
                    if (!names.Add(name))
                        throw new InvalidDataException("The map archive contains duplicate filenames: " + name);
                    if (result.Count >= MaxMapFiles)
                        throw new InvalidDataException("The map archive contains too many map files.");
                    if (entry.Length > MaxMapBytes || entry.Length > MaxTotalMapBytes - totalBytes)
                        throw new InvalidDataException("The downloaded maps exceed the supported size limit.");
                    using (Stream data = entry.Open())
                    using (MemoryStream contents = ReadBounded(data, (int)Math.Min(MaxMapBytes, MaxTotalMapBytes - totalBytes), "map file", null))
                    {
                        if (contents.Length != entry.Length)
                            throw new InvalidDataException("A downloaded map file is incomplete: " + name);
                        byte[] bytes = contents.ToArray();
                        totalBytes += bytes.Length;
                        result.Add(new MapFile(name, bytes));
                    }
                }
                if (result.Count == 0)
                    throw new InvalidDataException("No .txt files were found in the GitHub repository's maps folder.");
                return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private static MemoryStream ReadBounded(Stream source, int limit, string label, Stopwatch downloadTime)
        {
            if (source == null) throw new IOException("The download returned no content.");
            MemoryStream result = new MemoryStream();
            try
            {
                byte[] buffer = new byte[81920];
                int count;
                while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (downloadTime != null && downloadTime.Elapsed > TimeSpan.FromMinutes(2))
                        throw new IOException("The map download timed out.");
                    if (count > limit - result.Length)
                        throw new InvalidDataException("The " + label + " exceeds the supported size limit.");
                    result.Write(buffer, 0, count);
                }
                result.Position = 0;
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
    }
}
