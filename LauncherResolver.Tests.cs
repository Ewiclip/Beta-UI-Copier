using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EverQuestBetaUiCopier
{
    internal static class LauncherResolverTests
    {
        private static string testRoot;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: LauncherResolver.Tests.exe <temporary parent folder inside the current workspace>");
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
            testRoot = Path.Combine(parent, "eq-launcher-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            var tests = new Action[]
            {
                ResolvesDirectLaunchersWithoutExecution,
                ResolvesShortcutTargetInsteadOfWorkingDirectory,
                ResolvesEqgameShortcutWithArguments,
                RejectsUnrelatedExecutable,
                RejectsShortcutToUnrelatedExecutable,
                RejectsMissingSelectedFile,
                RejectsMissingShortcutTarget,
                RejectsLauncherWithoutGame,
                RejectsMalformedShortcut
            };
            int failures = 0;
            try
            {
                foreach (Action test in tests)
                {
                    try
                    {
                        test();
                        Console.WriteLine("PASS " + test.Method.Name);
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
                if (!cleanup.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetFileName(cleanup).StartsWith("eq-launcher-tests-", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to clean a path outside this test run.");
                Directory.Delete(cleanup, true);
            }
            Console.WriteLine((tests.Length - failures) + "/" + tests.Length + " launcher tests passed.");
            return failures == 0 ? 0 : 1;
        }

        private static void ResolvesDirectLaunchersWithoutExecution()
        {
            string game = NewGame();
            string launcher = Path.Combine(game, "LaunchPad.exe");
            string client = Path.Combine(game, "eqgame.exe");
            // These are empty, non-executable fixtures. Resolving them must not attempt to run them.
            Equal(game, LauncherResolver.GetGameDirectory(launcher), "Direct launcher folder");
            Equal(game, LauncherResolver.GetGameDirectory(client), "Direct game folder");
            Equal(0L, new FileInfo(launcher).Length, "Launcher fixture remains untouched");
            Equal(0L, new FileInfo(client).Length, "Game fixture remains untouched");
        }

        private static void ResolvesShortcutTargetInsteadOfWorkingDirectory()
        {
            string game = NewGame();
            string otherGame = NewGame();
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + " Live shortcut.LNK");
            MakeShortcut(shortcut, Path.Combine(game, "LaunchPad.exe"), otherGame, "--unused-test-argument");
            byte[] before = File.ReadAllBytes(shortcut);
            Equal(game, LauncherResolver.GetGameDirectory(shortcut), "Shortcut target folder, not its working directory");
            BytesEqual(before, File.ReadAllBytes(shortcut), "Resolving does not modify the shortcut");
            Equal(0L, new FileInfo(Path.Combine(game, "LaunchPad.exe")).Length, "Empty shortcut target remains untouched");
        }

        private static void ResolvesEqgameShortcutWithArguments()
        {
            string game = NewGame();
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + " Beta shortcut.lnk");
            MakeShortcut(shortcut, Path.Combine(game, "eqgame.exe"), game, "patchme");
            Equal(game, LauncherResolver.GetGameDirectory(shortcut), "eqgame shortcut arguments are not part of its file path");
        }

        private static void RejectsUnrelatedExecutable()
        {
            string game = NewGame();
            string unrelated = Path.Combine(game, "unrelated.exe");
            File.WriteAllBytes(unrelated, new byte[0]);
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(unrelated); }, "Choose an EverQuest shortcut");
        }

        private static void RejectsShortcutToUnrelatedExecutable()
        {
            string game = NewGame();
            string unrelated = Path.Combine(game, "unrelated.exe");
            File.WriteAllBytes(unrelated, new byte[0]);
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".lnk");
            MakeShortcut(shortcut, unrelated, game, "");
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(shortcut); }, "Choose an EverQuest shortcut");
        }

        private static void RejectsMissingSelectedFile()
        {
            string missing = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".lnk");
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(missing); }, "no longer exists");
        }

        private static void RejectsMissingShortcutTarget()
        {
            string game = NewGame();
            string missing = Path.Combine(game, "missing", "LaunchPad.exe");
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".lnk");
            MakeShortcut(shortcut, missing, game, "");
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(shortcut); }, "missing launcher");
        }

        private static void RejectsLauncherWithoutGame()
        {
            string folder = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + " incomplete install");
            Directory.CreateDirectory(folder);
            string launcher = Path.Combine(folder, "LaunchPad.exe");
            File.WriteAllBytes(launcher, new byte[0]);
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(launcher); }, "does not contain eqgame.exe");
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".lnk");
            MakeShortcut(shortcut, launcher, folder, "");
            ThrowsContaining(delegate { LauncherResolver.GetGameDirectory(shortcut); }, "does not contain eqgame.exe");
        }

        private static void RejectsMalformedShortcut()
        {
            string shortcut = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + ".lnk");
            File.WriteAllText(shortcut, "This is not a Windows shell shortcut.");
            byte[] before = File.ReadAllBytes(shortcut);
            bool rejected = false;
            try { LauncherResolver.GetGameDirectory(shortcut); }
            catch (Exception) { rejected = true; }
            if (!rejected) throw new Exception("Malformed shortcut must be rejected.");
            BytesEqual(before, File.ReadAllBytes(shortcut), "Malformed shortcut remains untouched");
        }

        private static string NewGame()
        {
            string folder = Path.Combine(testRoot, Guid.NewGuid().ToString("N") + " EverQuest test folder");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "eqgame.exe"), new byte[0]);
            File.WriteAllBytes(Path.Combine(folder, "LaunchPad.exe"), new byte[0]);
            return folder;
        }

        private static void MakeShortcut(string path, string target, string workingDirectory, string arguments)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void ThrowsContaining(Action action, string expectedMessage)
        {
            try { action(); }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) >= 0) return;
                throw new Exception("Expected error containing '" + expectedMessage + "', got: " + ex.Message, ex);
            }
            throw new Exception("Expected resolver to reject the selected file.");
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + ": expected " + expected + ", got " + actual);
        }

        private static void BytesEqual(byte[] expected, byte[] actual, string message)
        {
            if (expected.Length != actual.Length) throw new Exception(message);
            for (int i = 0; i < expected.Length; ++i)
                if (expected[i] != actual[i]) throw new Exception(message);
        }
    }
}
