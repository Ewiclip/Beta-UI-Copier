using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace EverQuestBetaUiCopier
{
    internal static class TransferCoreTests
    {
        private static string testRoot;

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: TransferCore.Tests.exe <temporary parent folder inside the current workspace>");
                return 2;
            }
            string workspace = Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string parent = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!parent.StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("The test folder must be inside the current workspace.");
                return 2;
            }
            Directory.CreateDirectory(parent);
            testRoot = Path.Combine(parent, "eq-beta-ui-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            var tests = new Action[]
            {
                FindsExactProfilesAndPairedCompanions,
                SupportsAllClassesAndServerNames,
                CopiesExactBytesAndBacksUpExistingBeta,
                CopiesUiWithoutCompanion,
                RejectsConflictingServersWithoutWriting,
                RejectsEmptyAndStaleSelectionWithoutWriting,
                RebuildsPlanFromFreshScan,
                ValidatesInstallFolders,
                SourceFailureLeavesBetaUntouched,
                FailedOverwriteRollsBackPriorFiles,
                RejectsDirectoryAtDestination,
                RejectsHardLinkedDestinationWithoutChangingLive,
                CopiesMapsAlongsideUiAndKeepsUnrelatedMaps,
                CopiesMapsIntoNewFolderAndReportsCounts,
                NoMapsLeavesMapsFolderUntouched,
                RejectsInvalidOrDuplicateMapsBeforeWrites,
                FailedMapOverwriteRollsBackUiAndMaps,
                RejectsBlockedMapsFolderBeforeWrites,
                FailedUiOverwriteRemovesNewMapsFolder,
                SourceFailureDoesNotCreateMapsFolder
            };
            int failures = 0;
            int skipped = 0;
            try
            {
                foreach (Action test in tests)
                {
                    try
                    {
                        test();
                        Console.WriteLine("PASS " + test.Method.Name);
                    }
                    catch (SkipTestException ex)
                    {
                        skipped++;
                        Console.WriteLine("SKIP " + test.Method.Name + ": " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        failures++;
                        Console.Error.WriteLine("FAIL " + test.Method.Name + ": " + ex);
                    }
                }
            }
            finally
            {
                string cleanup = Path.GetFullPath(testRoot);
                if (!cleanup.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to clean a path outside the test parent.");
                foreach (string path in Directory.GetFiles(cleanup, "*", SearchOption.AllDirectories))
                    File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(cleanup, true);
            }
            Console.WriteLine((tests.Length - failures - skipped) + "/" + tests.Length + " tests passed." +
                (skipped == 0 ? "" : " " + skipped + " skipped because this environment cannot create the required fixture."));
            return failures == 0 ? 0 : 1;
        }

        private static void FindsExactProfilesAndPairedCompanions()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Hotshott_xegony_SHM.ini", "ui");
            Put(f.Live, "hotshott_XEGONY_shm.INI", "companion");
            Put(f.Live, "UI_Alpha_bristlebane_CLR.ini", "ui only");
            Put(f.Live, "Orphan_xegony_WAR.ini", "no corresponding UI");
            Put(f.Live, "UI_Already_beta_SHM.ini", "beta profile");
            Put(f.Live, "UI_Already_BETA_WAR.ini", "beta profile");
            Put(f.Live, "UI_Diba_xegony_NEC_shrd.ini", "extra suffix");
            Put(f.Live, "UI_Unknown_xegony_NOPE.ini", "unknown class");
            Put(f.Live, "UI_Character123_xegony_SHM.ini", "invalid character");
            Put(f.Live, "UI_Hotshott_xegony.ini", "old unsuffixed profile");
            Put(f.Live, "UI_Hotshott_xegony_SHM.ini.bak", "backup");
            Put(f.Live, "UI_Bad_xegony_extra_SHM.ini", "extra part");
            var profiles = TransferCore.Scan(f.Live);
            Equal(2, profiles.Count, "Only exact supported UI profiles are selected");
            Equal("Alpha", profiles[0].Character, "Profiles sorted by character");
            Equal("Hotshott", profiles[1].Character, "Parsed character");
            Equal("xegony", profiles[1].Server, "Parsed Live server");
            Equal("SHM", profiles[1].ClassCode, "Parsed class");
            Equal("hotshott_XEGONY_shm.INI", Path.GetFileName(profiles[1].CompanionSourcePath), "Case insensitive companion match");
            Equal("UI_Hotshott_beta_SHM.ini", profiles[1].DestinationUiName, "UI destination");
            Equal("Hotshott_beta_SHM.ini", profiles[1].DestinationCompanionName, "Companion destination");
        }

        private static void SupportsAllClassesAndServerNames()
        {
            Fixture f = NewFixture();
            string[] classes = "WAR CLR PAL RNG SHD DRU MNK BRD ROG SHM NEC WIZ MAG ENC BST BER".Split(' ');
            foreach (string classCode in classes)
                Put(f.Live, "UI_Persona_server123_" + classCode + ".ini", classCode);
            Put(f.Live, "ui_lowercasename_xegony_shm.INI", "lowercase");
            var profiles = TransferCore.Scan(f.Live);
            Equal(17, profiles.Count, "All 16 classes and case-insensitive filenames");
            Equal("UI_lowercasename_beta_shm.ini", profiles[0].DestinationUiName, "Only server segment is changed");
        }

        private static void CopiesExactBytesAndBacksUpExistingBeta()
        {
            Fixture f = NewFixture();
            byte[] ui = new byte[] { 0, 255, 239, 187, 191, 65, 13, 10, 0, 200 };
            byte[] companion = Encoding.UTF8.GetBytes("[HotButtons]\r\nValue=xegony\r\n");
            byte[] oldBeta = Encoding.Unicode.GetBytes("old Beta UI");
            File.WriteAllBytes(Path.Combine(f.Live, "UI_Hotshott_xegony_SHM.ini"), ui);
            File.WriteAllBytes(Path.Combine(f.Live, "Hotshott_xegony_SHM.ini"), companion);
            File.WriteAllBytes(Path.Combine(f.Beta, "UI_Hotshott_beta_SHM.ini"), oldBeta);
            Put(f.Beta, "unrelated.ini", "preserve me");
            TransferResult result = TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live));
            Equal(2, result.CopiedFiles, "Copied pair");
            Equal(1, result.ReplacedFiles, "One overwritten target");
            Bytes(ui, Path.Combine(f.Beta, "UI_Hotshott_beta_SHM.ini"), "UI bytes preserved");
            Bytes(companion, Path.Combine(f.Beta, "Hotshott_beta_SHM.ini"), "Companion bytes preserved including server text");
            Bytes(ui, Path.Combine(f.Live, "UI_Hotshott_xegony_SHM.ini"), "Source UI unchanged");
            Bytes(companion, Path.Combine(f.Live, "Hotshott_xegony_SHM.ini"), "Source companion unchanged");
            Bytes(oldBeta, Path.Combine(result.BackupDirectory, "originals", "UI_Hotshott_beta_SHM.ini"), "Previous Beta bytes backed up");
            Equal("preserve me", File.ReadAllText(Path.Combine(f.Beta, "unrelated.ini")), "Unrelated file untouched");
            Assert(File.ReadAllText(Path.Combine(result.BackupDirectory, "transfer.txt")).Contains("SUCCESS"), "Success manifest");
            Assert(!Directory.Exists(Path.Combine(result.BackupDirectory, "staged")), "Staging cleaned");
        }

        private static void CopiesUiWithoutCompanion()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Solo_test_CLR.ini", "only ui");
            var profiles = TransferCore.Scan(f.Live);
            Assert(profiles[0].CompanionSourcePath == null, "Missing companion represented explicitly");
            TransferResult result = TransferCore.Copy(f.Live, f.Beta, profiles);
            Equal(1, result.CopiedFiles, "UI still copied");
            Assert(!File.Exists(Path.Combine(f.Beta, "Solo_beta_CLR.ini")), "No fabricated companion");
        }

        private static void RejectsConflictingServersWithoutWriting()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Duplicate_xegony_WAR.ini", "one");
            Put(f.Live, "ui_duplicate_bristlebane_war.INI", "two");
            Exception ex = Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live)); });
            Assert(ex.Message.Contains("More than one"), "Collision explains required choice");
            Equal(1, Directory.GetFiles(f.Beta).Length, "Only fixture game exists");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "No writes on collision");
            var single = new[] { TransferCore.Scan(f.Live)[0] };
            Equal(1, TransferCore.Copy(f.Live, f.Beta, single).CopiedFiles, "Selecting one server resolves collision");
        }

        private static void RejectsEmptyAndStaleSelectionWithoutWriting()
        {
            Fixture f = NewFixture();
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, new Profile[0]); });
            Put(f.Live, "UI_Stale_xegony_SHM.ini", "ui");
            var profiles = TransferCore.Scan(f.Live);
            File.Delete(profiles[0].UiSourcePath);
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, profiles); });
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "No backup or Beta writes on invalid selection");
        }

        private static void RebuildsPlanFromFreshScan()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Actual_xegony_SHM.ini", "actual ui");
            Put(f.Live, "Actual_xegony_SHM.ini", "actual companion");
            Put(f.Live, "unrelated.ini", "unrelated");
            Profile profile = TransferCore.Scan(f.Live)[0];
            profile.DestinationUiName = "unrelated.ini";
            profile.DestinationCompanionName = "bad.ini";
            profile.CompanionSourcePath = Path.Combine(f.Live, "unrelated.ini");
            TransferCore.Copy(f.Live, f.Beta, new[] { profile });
            Equal("actual ui", File.ReadAllText(Path.Combine(f.Beta, "UI_Actual_beta_SHM.ini")), "Derived UI filename");
            Equal("actual companion", File.ReadAllText(Path.Combine(f.Beta, "Actual_beta_SHM.ini")), "Derived companion path");
            Assert(!File.Exists(Path.Combine(f.Beta, "unrelated.ini")), "Mutated UI paths ignored");
        }

        private static void ValidatesInstallFolders()
        {
            Fixture f = NewFixture();
            TransferCore.ValidateFolders(f.Live, f.Beta);
            Throws(delegate { TransferCore.ValidateFolders(f.Live, f.Live + Path.DirectorySeparatorChar); });
            Throws(delegate { TransferCore.ValidateFolders(f.Live, Path.Combine(f.Beta, "missing")); });
            File.Delete(Path.Combine(f.Beta, "eqgame.exe"));
            Throws(delegate { TransferCore.ValidateFolders(f.Live, f.Beta); });
        }

        private static void SourceFailureLeavesBetaUntouched()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new alpha");
            Put(f.Live, "UI_Zulu_xegony_SHM.ini", "new zulu");
            Put(f.Beta, "UI_Alpha_beta_SHM.ini", "old alpha");
            using (var locked = new FileStream(Path.Combine(f.Live, "UI_Zulu_xegony_SHM.ini"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live)); });
            Equal("old alpha", File.ReadAllText(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini")), "All sources staged before any overwrite");
            Assert(!File.Exists(Path.Combine(f.Beta, "UI_Zulu_beta_SHM.ini")), "No incomplete new target");
        }

        private static void FailedOverwriteRollsBackPriorFiles()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new alpha ui");
            Put(f.Live, "Alpha_xegony_SHM.ini", "new alpha companion");
            Put(f.Live, "UI_Zulu_xegony_SHM.ini", "new zulu ui");
            Put(f.Beta, "Alpha_beta_SHM.ini", "old alpha companion");
            Put(f.Beta, "UI_Zulu_beta_SHM.ini", "old zulu ui");
            string readOnly = Path.Combine(f.Beta, "UI_Zulu_beta_SHM.ini");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            Exception ex = Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live)); });
            Assert(ex.Message.Contains("rolled back"), "Successful rollback reported");
            Assert(!File.Exists(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini")), "New target removed during rollback");
            Equal("old alpha companion", File.ReadAllText(Path.Combine(f.Beta, "Alpha_beta_SHM.ini")), "Previously overwritten target restored");
            Equal("old zulu ui", File.ReadAllText(readOnly), "Failed read-only target unchanged");
            Assert((File.GetAttributes(readOnly) & FileAttributes.ReadOnly) != 0, "Read-only attribute preserved");
        }

        private static void RejectsDirectoryAtDestination()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new alpha");
            Directory.CreateDirectory(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini"));
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live)); });
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "Unsafe destination rejected before writes");
        }

        private static void RejectsHardLinkedDestinationWithoutChangingLive()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "source bytes must stay unchanged");
            string source = Path.Combine(f.Live, "UI_Alpha_xegony_SHM.ini");
            string target = Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini");
            if (!CreateHardLink(target, source, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                string explanation = "Could not create the isolated hard-link fixture (Windows error " + error + "): " + new System.ComponentModel.Win32Exception(error).Message;
                if (error == 1 || error == 5 || error == 50 || error == 1314)
                    throw new SkipTestException(explanation);
                throw new Exception(explanation);
            }
            byte[] original = File.ReadAllBytes(source);
            Exception ex = Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live)); });
            Assert(ex.Message.Contains("multiple hard links"), "Hard-link rejection explains why copying is unsafe");
            Bytes(original, source, "Hard-linked Live source unchanged");
            Bytes(original, target, "Hard-linked Beta target unchanged");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "Hard-linked target rejected before writes");
        }

        private static Fixture NewFixture()
        {
            string root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            var f = new Fixture { Live = Path.Combine(root, "Live"), Beta = Path.Combine(root, "Beta") };
            Directory.CreateDirectory(f.Live);
            Directory.CreateDirectory(f.Beta);
            Put(f.Live, "eqgame.exe", "fixture only");
            Put(f.Beta, "eqgame.exe", "fixture only");
            return f;
        }

        private static void CopiesMapsAlongsideUiAndKeepsUnrelatedMaps()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new UI");
            Put(f.Live, "Alpha_xegony_SHM.ini", "new character");
            Put(f.Beta, "UI_Alpha_beta_SHM.ini", "old UI");
            string mapFolder = Path.Combine(f.Beta, "maps");
            Directory.CreateDirectory(mapFolder);
            Put(mapFolder, "zone.txt", "old zone");
            Put(mapFolder, "unrelated.txt", "untouched map");
            string customMaps = Path.Combine(mapFolder, "custom");
            Directory.CreateDirectory(customMaps);
            Put(customMaps, "zone.txt", "custom map");
            byte[] downloaded = new byte[] { 239, 187, 191, 0, 255, 76, 13, 10, 200 };
            var maps = new[] { new MapFile("zone.txt", downloaded), new MapFile("zone_1.txt", new byte[0]) };

            TransferResult result = TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), maps);

            Equal(4, result.CopiedFiles, "Combined total");
            Equal(2, result.CopiedUiFiles, "UI count excludes maps");
            Equal(2, result.CopiedMapFiles, "Map count");
            Equal(2, result.ReplacedFiles, "One UI and one map overwritten");
            Equal("new UI", File.ReadAllText(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini")), "UI stays in game root");
            Equal("new character", File.ReadAllText(Path.Combine(f.Beta, "Alpha_beta_SHM.ini")), "Character stays in game root");
            Bytes(downloaded, Path.Combine(mapFolder, "zone.txt"), "Map bytes copied unchanged");
            Bytes(new byte[0], Path.Combine(mapFolder, "zone_1.txt"), "Empty map layer preserved");
            Equal("old zone", File.ReadAllText(Path.Combine(result.BackupDirectory, "originals", "maps", "zone.txt")), "Map backup preserves maps subfolder");
            Equal("old UI", File.ReadAllText(Path.Combine(result.BackupDirectory, "originals", "UI_Alpha_beta_SHM.ini")), "UI backup stays in originals root");
            Equal("untouched map", File.ReadAllText(Path.Combine(mapFolder, "unrelated.txt")), "Unrelated map retained");
            Equal("custom map", File.ReadAllText(Path.Combine(customMaps, "zone.txt")), "Custom map folder retained");
            Assert(!File.Exists(Path.Combine(f.Beta, "zone.txt")), "Map never written to game root");
            Equal("new UI", File.ReadAllText(Path.Combine(f.Live, "UI_Alpha_xegony_SHM.ini")), "Live remains unchanged");
            string manifest = File.ReadAllText(Path.Combine(result.BackupDirectory, "transfer.txt"));
            Assert(manifest.Contains(Path.Combine("maps", "zone.txt")), "Manifest lists relative map paths");
            Assert(manifest.Contains(MapDownloader.ArchiveUrl), "Manifest records download source");
            Assert(manifest.Contains("preserving subfolders"), "Manifest explains map restoration");
        }

        private static void CopiesMapsIntoNewFolderAndReportsCounts()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new UI");
            byte[] downloaded = Encoding.UTF8.GetBytes("L 1, 2, 3, 4, 5, 6, 0, 0, 0\n");
            TransferResult result = TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), new[] { new MapFile("newzone.txt", downloaded) });
            Equal(2, result.CopiedFiles, "UI and map both counted");
            Equal(1, result.CopiedUiFiles, "One UI file");
            Equal(1, result.CopiedMapFiles, "One map file");
            Equal(0, result.ReplacedFiles, "New targets need no backups");
            Bytes(downloaded, Path.Combine(f.Beta, "maps", "newzone.txt"), "Maps folder created and populated");
        }

        private static void NoMapsLeavesMapsFolderUntouched()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "UI");
            TransferResult result = TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live));
            Assert(!Directory.Exists(Path.Combine(f.Beta, "maps")), "No maps directory created with feature off");
            Equal(1, result.CopiedUiFiles, "Legacy copy returns UI count");
            Equal(0, result.CopiedMapFiles, "Legacy copy has no maps");
            Put(f.Beta, "maps", "unrelated file named maps");
            TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), new MapFile[0]);
            Equal("unrelated file named maps", File.ReadAllText(Path.Combine(f.Beta, "maps")), "Feature off does not inspect or alter maps path");
        }

        private static void RejectsInvalidOrDuplicateMapsBeforeWrites()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "UI");
            var profiles = TransferCore.Scan(f.Live);
            var tampered = new MapFile("safe.txt", new byte[0]);
            typeof(MapFile).GetProperty("Name").SetValue(tampered, "../escaped.txt", null);
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, profiles, new[] { tampered }); });
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, profiles, new MapFile[] { null }); });
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, profiles, null); });
            Exception duplicate = Throws(delegate
            {
                TransferCore.Copy(f.Live, f.Beta, profiles, new[] { new MapFile("zone.txt", new byte[0]), new MapFile("ZONE.TXT", new byte[0]) });
            });
            Assert(duplicate.Message.Contains("More than one downloaded map"), "Duplicate explains failure");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "Invalid maps rejected before backup writes");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "maps")), "Invalid maps cannot create target folder");
            Assert(!File.Exists(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini")), "Invalid maps cannot partially copy UI");
        }

        private static void FailedMapOverwriteRollsBackUiAndMaps()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new UI");
            Put(f.Live, "Alpha_xegony_SHM.ini", "new character");
            Put(f.Beta, "UI_Alpha_beta_SHM.ini", "old UI");
            string mapFolder = Path.Combine(f.Beta, "maps");
            Directory.CreateDirectory(mapFolder);
            Put(mapFolder, "first.txt", "old first map");
            Put(mapFolder, "last.txt", "old last map");
            string lockedMap = Path.Combine(mapFolder, "last.txt");
            File.SetAttributes(lockedMap, FileAttributes.ReadOnly);
            var maps = new[]
            {
                new MapFile("first.txt", Encoding.UTF8.GetBytes("new first map")),
                new MapFile("new.txt", Encoding.UTF8.GetBytes("newly added map")),
                new MapFile("last.txt", Encoding.UTF8.GetBytes("new last map"))
            };
            Exception failure = Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), maps); });
            Assert(failure.Message.Contains("rolled back"), "Combined rollback reported");
            Equal("old UI", File.ReadAllText(Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini")), "UI overwrite restored after map failure");
            Assert(!File.Exists(Path.Combine(f.Beta, "Alpha_beta_SHM.ini")), "New character removed after map failure");
            Equal("old first map", File.ReadAllText(Path.Combine(mapFolder, "first.txt")), "Earlier overwritten map restored");
            Assert(!File.Exists(Path.Combine(mapFolder, "new.txt")), "Earlier new map removed");
            Equal("old last map", File.ReadAllText(lockedMap), "Failing map untouched");
            Assert((File.GetAttributes(lockedMap) & FileAttributes.ReadOnly) != 0, "Map read-only attribute retained");
            string backup = Directory.GetDirectories(Path.Combine(f.Beta, "BetaUI-Backups"))[0];
            Equal("old first map", File.ReadAllText(Path.Combine(backup, "originals", "maps", "first.txt")), "Map originals remain available after failure");
        }

        private static void RejectsBlockedMapsFolderBeforeWrites()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "UI");
            Put(f.Beta, "maps", "occupied");
            Exception failure = Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), new[] { new MapFile("zone.txt", new byte[0]) }); });
            Assert(failure.Message.Contains("Beta maps folder"), "Blocked maps directory explained");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "BetaUI-Backups")), "Blocked maps path rejected before any writes");
            Equal("occupied", File.ReadAllText(Path.Combine(f.Beta, "maps")), "Blocking file left untouched");
        }

        private static void FailedUiOverwriteRemovesNewMapsFolder()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new UI");
            Put(f.Beta, "UI_Alpha_beta_SHM.ini", "old UI");
            string target = Path.Combine(f.Beta, "UI_Alpha_beta_SHM.ini");
            File.SetAttributes(target, FileAttributes.ReadOnly);
            Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), new[] { new MapFile("zone.txt", new byte[0]) }); });
            Equal("old UI", File.ReadAllText(target), "Existing UI unchanged");
            Assert(!Directory.Exists(Path.Combine(f.Beta, "maps")), "New empty maps directory removed on rollback");
        }

        private static void SourceFailureDoesNotCreateMapsFolder()
        {
            Fixture f = NewFixture();
            Put(f.Live, "UI_Alpha_xegony_SHM.ini", "new UI");
            string source = Path.Combine(f.Live, "UI_Alpha_xegony_SHM.ini");
            using (var locked = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws(delegate { TransferCore.Copy(f.Live, f.Beta, TransferCore.Scan(f.Live), new[] { new MapFile("zone.txt", new byte[0]) }); });
            Assert(!Directory.Exists(Path.Combine(f.Beta, "maps")), "Map directory is created only after every source and backup is ready");
        }

        private static void Put(string directory, string name, string contents)
        {
            File.WriteAllText(Path.Combine(directory, name), contents);
        }

        private static void Bytes(byte[] expected, string path, string message)
        {
            Assert(expected.SequenceEqual(File.ReadAllBytes(path)), message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + ": expected " + expected + ", got " + actual);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static Exception Throws(Action action)
        {
            try { action(); }
            catch (Exception ex) { return ex; }
            throw new Exception("Expected operation to throw.");
        }

        private sealed class Fixture
        {
            internal string Live;
            internal string Beta;
        }

        private sealed class SkipTestException : Exception
        {
            internal SkipTestException(string message) : base(message) { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
    }
}
