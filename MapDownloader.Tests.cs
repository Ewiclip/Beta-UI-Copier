using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace EverQuestBetaUiCopier
{
    internal static class MapDownloaderTests
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--live")
            {
                try
                {
                    List<MapFile> maps = MapDownloader.Download(Console.WriteLine);
                    Console.WriteLine("LIVE DOWNLOAD: " + maps.Count + " map files, " + maps.Sum(m => (long)m.Contents.Length) + " bytes. No files were installed.");
                    foreach (MapFile map in maps) Console.WriteLine(map.Name);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            }
            Action[] tests = {
                ReadsOnlyTopLevelMapsAndPreservesBytes,
                RejectsEmptyOrUnrelatedArchive,
                RejectsMalformedZip,
                RejectsUnsafeEntryPaths,
                RejectsDuplicateNamesIgnoringCase,
                RejectsMoreThanOneRepositoryRoot,
                RejectsOversizedMap,
                RejectsTooManyMaps,
                RejectsUnsafeMapNames,
                RejectsNullMapContents
            };
            int failures = 0;
            foreach (Action test in tests)
            {
                try { test(); Console.WriteLine("PASS " + test.Method.Name); }
                catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + ex); }
            }
            Console.WriteLine((tests.Length - failures) + "/" + tests.Length + " map download tests passed.");
            return failures == 0 ? 0 : 1;
        }

        private static void ReadsOnlyTopLevelMapsAndPreservesBytes()
        {
            byte[] content = { 0xef, 0xbb, 0xbf, 76, 32, 49, 44, 50, 13, 10 };
            using (MemoryStream stream = Archive(new Dictionary<string, byte[]> {
                { "repo-main/", new byte[0] },
                { "repo-main/Program.cs", Bytes("ignore source") },
                { "repo-main/maps/", new byte[0] },
                { "repo-main/maps/zone_1.TXT", content },
                { "repo-main/maps/alpha.txt", new byte[0] },
                { "repo-main/maps/readme.md", Bytes("ignore documentation") },
                { "repo-main/maps/nested/other.txt", Bytes("ignore nested map") },
                { "repo-main/elsewhere/map.txt", Bytes("ignore other folder") }
            }))
            {
                List<MapFile> maps = MapDownloader.ReadArchive(stream);
                Equal(2, maps.Count, "Only supported maps included");
                Equal("alpha.txt", maps[0].Name, "Sorted names");
                Equal("zone_1.TXT", maps[1].Name, "Original filename case");
                Require(content.SequenceEqual(maps[1].Contents), "Original bytes preserved");
            }
        }

        private static void RejectsEmptyOrUnrelatedArchive()
        {
            using (MemoryStream empty = Archive(new Dictionary<string, byte[]>()))
                Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(empty); });
            using (MemoryStream unrelated = Archive(new Dictionary<string, byte[]> { { "repo-main/README.md", Bytes("No maps") } }))
                Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(unrelated); });
        }

        private static void RejectsMalformedZip()
        {
            using (MemoryStream stream = new MemoryStream(Bytes("<html>not a zip</html>")))
                Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsUnsafeEntryPaths()
        {
            string[] invalid = { "repo-main/maps/../escape.txt", "repo-main/maps/folder/../../escape.txt",
                "repo-main\\maps\\escape.txt", "/maps/escape.txt", "repo-main/maps/zone.txt:stream.txt", "repo-main//maps/zone.txt" };
            foreach (string path in invalid)
                using (MemoryStream stream = Archive(new Dictionary<string, byte[]> { { path, Bytes("bad") } }))
                    Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsDuplicateNamesIgnoringCase()
        {
            using (MemoryStream stream = Archive(new Dictionary<string, byte[]> {
                { "repo-main/maps/zone.txt", Bytes("one") }, { "repo-main/maps/ZONE.TXT", Bytes("two") }
            })) Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsMoreThanOneRepositoryRoot()
        {
            using (MemoryStream stream = Archive(new Dictionary<string, byte[]> {
                { "repo-main/maps/zone.txt", Bytes("one") }, { "other-main/maps/other.txt", Bytes("two") }
            })) Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsOversizedMap()
        {
            using (MemoryStream stream = Archive(new Dictionary<string, byte[]> {
                { "repo-main/maps/zone.txt", new byte[MapDownloader.MaxMapBytes + 1] }
            })) Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsTooManyMaps()
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            for (int i = 0; i <= MapDownloader.MaxMapFiles; i++) files.Add("repo-main/maps/zone_" + i + ".txt", new byte[0]);
            using (MemoryStream stream = Archive(files)) Throws<InvalidDataException>(delegate { MapDownloader.ReadArchive(stream); });
        }

        private static void RejectsUnsafeMapNames()
        {
            string[] invalid = { null, "", " ", ".txt", "../zone.txt", "folder/zone.txt", "folder\\zone.txt", "C:\\zone.txt",
                "zone.txt:stream.txt", "zone.txt ", "zone.txt.", "zone?.txt", "zone.exe", "CON.txt", "CON.extra.txt",
                "com1.txt", "LPT9.txt", "COM\u00b9.txt", "NUL.txt", "AUX.txt", "PRN.txt", "CON .txt" };
            foreach (string name in invalid) Throws<InvalidDataException>(delegate { MapFile.ValidateName(name); });
            MapFile.ValidateName("poguile_2.txt");
            MapFile.ValidateName("COM10.txt");
        }

        private static void RejectsNullMapContents()
        {
            Throws<ArgumentNullException>(delegate { new MapFile("zone.txt", null); });
        }

        private static MemoryStream Archive(Dictionary<string, byte[]> files)
        {
            MemoryStream stream = new MemoryStream();
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                foreach (KeyValuePair<string, byte[]> file in files)
                    using (Stream content = zip.CreateEntry(file.Key).Open()) content.Write(file.Value, 0, file.Value.Length);
            stream.Position = 0;
            return stream;
        }

        private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Equal<T>(T expected, T actual, string message) { Require(EqualityComparer<T>.Default.Equals(expected, actual), message + ": expected " + expected + ", got " + actual); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}
