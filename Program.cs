using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace FiveMPermissionFixer;

internal static class Program
{
    private const string AsiName = "SirenSetting_Limit_Adjuster.asi";
    private const string AsiResource = "FiveMPermissionFixer.Assets.SirenSetting_Limit_Adjuster.asi";

    private static string? GtaPath;
    private static string? CitizenIni;

    private static void Main()
    {
        Console.Title = "FiveM Permission Fixer";
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.CursorVisible = false; } catch { /* not all terminals support this */ }

        Screen(() =>
        {
            Header();
            SubText("Searching all accessible drives for a FiveM install...");
            Console.WriteLine();
        });

        var found = ScanDrives();

        if (!found)
        {
            Console.WriteLine();
            Fail("FiveM was not found.");
            Muted("The program searched all accessible drives for:");
            Muted(@"  <any folder>\FiveM\FiveM.app\CitizenFX.ini");
            Muted(@"It also checked the normal %LOCALAPPDATA%\FiveM location.");
            Pause();
            return;
        }

        Console.WriteLine();
        ShowFoundBanner();

        while (true)
        {
            Screen(() =>
            {
                Header();
                ShowFoundBanner();
                Menu();
            });

            Console.Write("  > ");
            string choice = Console.ReadLine()?.Trim() ?? "";
            Console.WriteLine();
            Line();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    Section("CHECK GTA V PERMISSIONS");
                    CheckWriteAccess();
                    break;
                case "2":
                    Section("FIX GTA V PERMISSIONS");
                    FixPermissions();
                    break;
                case "3":
                    Section("CHECK SirenSettings.log");
                    CheckSirenLog();
                    break;
                case "4":
                    Section("INSTALL BUNDLED ASI");
                    InstallAsi();
                    break;
                case "5":
                    Section("RUNNING ALL FIXES");
                    FixPermissions();
                    Console.WriteLine();
                    CheckSirenLog();
                    Console.WriteLine();
                    InstallAsi();
                    break;
                case "6":
                    GtaPath = null;
                    CitizenIni = null;
                    Screen(() =>
                    {
                        Header();
                        SubText("Searching all accessible drives again...");
                        Console.WriteLine();
                    });
                    ScanDrives();
                    break;
                case "0":
                    Screen(() =>
                    {
                        Header();
                        SubText("See you on the streets of Los Santos.");
                    });
                    try { Console.CursorVisible = true; } catch { }
                    return;
                default:
                    Warn("Invalid option.");
                    break;
            }

            Console.WriteLine();
            Pause();
        }
    }


    private static bool ScanDrives()
    {
        // Universal detection, but STOPS THE INSTANT a working install is found:
        // 1. Check %LOCALAPPDATA%\FiveM first - this is where it is 95% of the time.
        // 2. Check the ROOT of every drive (including G:\FiveM).
        // 3. Check several common custom-install locations on every drive.
        // 4. Only if none of the above worked anywhere, recursively search each drive
        //    for a folder named FiveM.
        //
        // Each candidate is validated (CitizenFX.ini exists + IVPath resolves to a real
        // GTA V folder, falling back to a full-drive GTA V search if IVPath is stale) the
        // moment it's found. As soon as one fully works, we return immediately - no more
        // scanning of other locations, other drives, or slow recursive searches.

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool TryUseCandidate(string path, string reason)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            if (!Directory.Exists(full) || !seen.Add(full))
                return false;

            string ini = Path.Combine(full, "FiveM.app", "CitizenFX.ini");

            if (!File.Exists(ini))
            {
                DimSuffix($"  [CHECK] {full} ({reason}) - no CitizenFX.ini, skipping");
                return false;
            }

            Good($"  [FOUND] FiveM install ({reason}): {full}");
            Muted($"          Config: {ini}");

            string? ivPath = ReadIniValue(ini, "IVPath");

            if (!string.IsNullOrWhiteSpace(ivPath))
            {
                string expanded = Environment.ExpandEnvironmentVariables(ivPath.Trim().Trim('"'));

                if (Directory.Exists(expanded))
                {
                    CitizenIni = ini;
                    GtaPath = expanded;
                    Good($"          GTA V found: {GtaPath}");
                    return true;
                }

                Warn($"          GTA V folder does not exist: {expanded}");
            }
            else
            {
                Warn("          IVPath missing.");
            }

            Muted("          Searching all drives for a GTA V install as a fallback...");

            if (TryFindGta5Fallback(out string? fallbackPath) && fallbackPath is not null)
            {
                CitizenIni = ini;
                GtaPath = fallbackPath;
                Good($"          GTA V found via fallback search: {GtaPath}");
                return true;
            }

            Warn("          No GTA V install could be found on any drive.");
            return false;
        }

        // 1. Normal Windows install location - by far the most common, check it first.
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            if (TryUseCandidate(Path.Combine(localAppData, "FiveM"), "%LOCALAPPDATA%"))
                return true;
        }

        var readyDrives = DriveInfo.GetDrives().Where(d =>
        {
            if (d.IsReady) return true;
            Muted($"  [SKIP] {d.Name} - drive not ready");
            return false;
        }).ToList();

        // PASS 1: fast checks only (drive root + common install folders) across EVERY
        // drive before touching the slow recursive search on any of them. This is what
        // makes it move on to D:\, E:\, etc. right away instead of getting stuck crawling
        // all of C:\ first.
        foreach (DriveInfo drive in readyDrives)
        {
            string root = drive.RootDirectory.FullName;
            Console.WriteLine();
            Console.WriteLine($"  [DRIVE] Checking {root}");

            if (TryUseCandidate(Path.Combine(root, "FiveM"), "drive root"))
                return true;

            string[] commonPaths =
            {
                Path.Combine(root, "Games", "FiveM"),
                Path.Combine(root, "Game", "FiveM"),
                Path.Combine(root, "FiveM"),
                Path.Combine(root, "Program Files", "FiveM"),
                Path.Combine(root, "Program Files (x86)", "FiveM"),
                Path.Combine(root, "Program Files", "FiveM.app"),
                Path.Combine(root, "Users", Environment.UserName, "FiveM"),
                Path.Combine(root, "Users", Environment.UserName, "AppData", "Local", "FiveM")
            };

            foreach (string common in commonPaths)
            {
                if (TryUseCandidate(common, "common location"))
                    return true;
            }
        }

        // PASS 2: only reached if NOTHING was found anywhere via the fast checks above.
        // Now, and only now, do the slow recursive search - still one drive at a time,
        // still moving on to the next drive if a given drive turns up nothing.
        Console.WriteLine();
        Muted("  Nothing found via quick checks - falling back to a deeper search...");

        foreach (DriveInfo drive in readyDrives)
        {
            string root = drive.RootDirectory.FullName;
            Console.WriteLine();
            Console.WriteLine($"  [SEARCH] Looking deeper in {root}...");

            try
            {
                foreach (string directory in EnumerateDirectoriesSafe(root))
                {
                    if (!Path.GetFileName(directory).Equals("FiveM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (TryUseCandidate(directory, "deep search"))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Warn($"  Could not completely scan {root}: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// Scans every accessible drive for a real GTA V install (a folder containing GTA5.exe).
    /// Used as a fallback when the IVPath recorded in CitizenFX.ini is missing or stale
    /// (e.g. GTA V got moved to a different drive than the one FiveM originally saw it on).
    /// Just like ScanDrives(), if nothing is found on C:\ it keeps checking D:\, E:\, etc.
    /// </summary>
    private static bool TryFindGta5Fallback(out string? gtaPath)
    {
        gtaPath = null;
        string? found = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool TryUseFolder(string path, string reason)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return false;
            }

            if (!Directory.Exists(full) || !seen.Add(full))
                return false;

            if (!File.Exists(Path.Combine(full, "GTA5.exe")))
            {
                DimSuffix($"  [CHECK] {full} ({reason}) - no GTA5.exe, skipping");
                return false;
            }

            Good($"  [FOUND] GTA V install ({reason}): {full}");
            found = full;
            return true;
        }

        var readyDrives = DriveInfo.GetDrives().Where(d =>
        {
            if (d.IsReady) return true;
            Muted($"  [SKIP] {d.Name} - drive not ready");
            return false;
        }).ToList();

        // PASS 1: fast checks only (common launcher install folders) across EVERY drive
        // before touching the slow recursive search on any of them.
        foreach (DriveInfo drive in readyDrives)
        {
            string root = drive.RootDirectory.FullName;
            Console.WriteLine();
            Console.WriteLine($"  [DRIVE] Checking {root} for GTA V");

            string[] commonPaths =
            {
                Path.Combine(root, "Grand Theft Auto V"),
                Path.Combine(root, "Games", "Grand Theft Auto V"),
                Path.Combine(root, "Game", "Grand Theft Auto V"),
                Path.Combine(root, "SteamLibrary", "steamapps", "common", "Grand Theft Auto V"),
                Path.Combine(root, "Program Files", "Steam", "steamapps", "common", "Grand Theft Auto V"),
                Path.Combine(root, "Program Files (x86)", "Steam", "steamapps", "common", "Grand Theft Auto V"),
                Path.Combine(root, "Steam", "steamapps", "common", "Grand Theft Auto V"),
                Path.Combine(root, "Program Files", "Epic Games", "GTAV"),
                Path.Combine(root, "Epic Games", "GTAV"),
                Path.Combine(root, "Program Files", "Rockstar Games", "Grand Theft Auto V"),
                Path.Combine(root, "Program Files (x86)", "Rockstar Games", "Grand Theft Auto V"),
                Path.Combine(root, "Rockstar Games", "Grand Theft Auto V"),
            };

            foreach (string common in commonPaths)
            {
                if (TryUseFolder(common, "common location"))
                {
                    gtaPath = found;
                    return true;
                }
            }
        }

        // PASS 2: only reached if NOTHING was found anywhere via the fast checks above.
        // Now, and only now, do the slow recursive search - one drive at a time, moving
        // on to the next drive if the current one turns up nothing.
        Console.WriteLine();
        Muted("  Nothing found via quick checks - falling back to a deeper search for GTA V...");

        foreach (DriveInfo drive in readyDrives)
        {
            string root = drive.RootDirectory.FullName;
            Console.WriteLine();
            Console.WriteLine($"  [SEARCH] Looking deeper in {root} for GTA5.exe...");

            try
            {
                foreach (string directory in EnumerateDirectoriesSafe(root))
                {
                    string name = Path.GetFileName(directory);

                    bool looksLikeGta =
                        name.Equals("Grand Theft Auto V", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("GTAV", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("GTA V", StringComparison.OrdinalIgnoreCase);

                    if (!looksLikeGta)
                        continue;

                    if (TryUseFolder(directory, "deep search"))
                    {
                        gtaPath = found;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Warn($"  Could not completely scan {root}: {ex.Message}");
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (string directory in directories)
            {
                FileAttributes attributes;

                try
                {
                    attributes = File.GetAttributes(directory);
                }
                catch
                {
                    continue;
                }

                // Do not follow junctions/symlinks/reparse points.
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    continue;

                yield return directory;
                pending.Push(directory);
            }
        }
    }

    private static void CheckWriteAccess()
    {
        if (!RequireGta()) return;

        Muted("Testing write access to:");
        Muted(GtaPath!);

        string test = Path.Combine(GtaPath!, $".fivem_permission_test_{Guid.NewGuid():N}.tmp");

        Spinner("Probing folder permissions");

        try
        {
            File.WriteAllText(test, "FiveM Permission Fixer test");
            File.Delete(test);
            Good("WRITE ACCESS OK - current Windows account can modify the GTA V folder.");
        }
        catch (Exception ex)
        {
            Fail("WRITE ACCESS FAILED.");
            Muted($"Reason: {ex.Message}");
            Muted("Use option 2 to repair the permission.");
        }
    }

    private static void FixPermissions()
    {
        if (!RequireGta()) return;

        Muted("This will grant the CURRENT Windows account Modify permission");
        Muted("on the GTA V folder, subfolders and files.");
        Console.WriteLine();
        Console.Write("  Continue? [Y/N]: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
        {
            Warn("Cancelled.");
            return;
        }

        string user = WindowsIdentity.GetCurrent().Name;

        Console.WriteLine();
        Muted($"Account: {user}");
        Muted("Requesting administrator permission (UAC prompt incoming)...");

        string args = $"\"{GtaPath}\" /grant \"{user}\":(OI)(CI)M /T /C";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "icacls.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Spinner("Applying ACLs (this can take a moment on large installs)");

            using Process? process = Process.Start(psi);

            if (process is null)
            {
                Fail("Could not start Windows permission repair.");
                return;
            }

            process.WaitForExit();

            if (process.ExitCode == 0)
                Good("Permissions repaired successfully.");
            else
                Warn($"icacls returned exit code {process.ExitCode}.");

            Console.WriteLine();
            CheckWriteAccess();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Warn("Administrator request was cancelled.");
        }
        catch (Exception ex)
        {
            Fail($"Permission repair failed: {ex.Message}");
        }
    }

    private static void CheckSirenLog()
    {
        if (!RequireGta()) return;

        string log = Path.Combine(GtaPath!, "SirenSettings.log");

        if (!File.Exists(log))
        {
            Good("SirenSettings.log does not currently exist.");
            return;
        }

        Muted($"Found: {log}");

        try
        {
            FileAttributes attrs = File.GetAttributes(log);
            Muted($"Attributes: {attrs}");

            bool restricted =
                attrs.HasFlag(FileAttributes.ReadOnly) ||
                attrs.HasFlag(FileAttributes.Hidden) ||
                attrs.HasFlag(FileAttributes.System);

            if (restricted)
                Warn("The log has restrictive file attributes.");
            else
                Good("The log has normal file attributes.");
        }
        catch (Exception ex)
        {
            Warn($"Could not inspect the log: {ex.Message}");
        }

        Console.Write("  Remove SirenSettings.log? [Y/N]: ");

        if (!string.Equals(Console.ReadLine()?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            File.SetAttributes(log, FileAttributes.Normal);
            File.Delete(log);
            Good("SirenSettings.log removed.");
        }
        catch (Exception ex)
        {
            Fail($"Could not remove the log: {ex.Message}");
            Muted("Try option 2 first, then retry this option.");
        }
    }

    private static void InstallAsi()
    {
        if (!RequireGta()) return;

        string target = Path.Combine(GtaPath!, AsiName);

        try
        {
            byte[] asi = GetAsi();

            if (File.Exists(target))
            {
                string backup = target + ".backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(target, backup);
                Muted("Existing ASI backed up to:");
                Muted(backup);
            }

            Spinner("Writing ASI to disk");
            File.WriteAllBytes(target, asi);

            Good("ASI installed:");
            Muted(target);
            Muted($"Size: {asi.Length:N0} bytes");
        }
        catch (UnauthorizedAccessException)
        {
            Fail("Windows denied access to the GTA V folder.");
            Muted("Run option 2 first, then install the ASI.");
        }
        catch (Exception ex)
        {
            Fail($"ASI installation failed: {ex.Message}");
        }
    }

    private static byte[] GetAsi()
    {
        using Stream? stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(AsiResource);

        if (stream is null)
            throw new FileNotFoundException("Bundled ASI resource was not found.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool RequireGta()
    {
        if (!string.IsNullOrWhiteSpace(GtaPath) && Directory.Exists(GtaPath))
            return true;

        Fail("GTA V path is not currently detected.");
        Muted("Run option 6 to scan again.");
        return false;
    }

    private static string? ReadIniValue(string file, string key)
    {
        foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            string line = raw.Trim();

            if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                continue;

            int equals = line.IndexOf('=');

            if (equals <= 0)
                continue;

            string name = line[..equals].Trim();

            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;

            return line[(equals + 1)..].Trim();
        }

        return null;
    }


    private static void Screen(Action render)
    {
        Console.Clear();
        render();
    }

    private static readonly string[] Banner =
    {
        "##### ### #   # ##### #   #",
        "#      #  #   # #     ## ##",
        "####   #  #   # ####  # # #",
        "#      #   # #  #     #   #",
        "#     ###   #   ##### #   #",
    };


    private static readonly (int Start, int Length)[] LetterSpans =
    {
        (0, 5),   // F
        (6, 3),   // I
        (10, 5),  // V
        (16, 5),  // E
        (22, 5),  // M
    };

    private static readonly ConsoleColor[] Rainbow =
    {
        ConsoleColor.Red,
        ConsoleColor.DarkYellow,
        ConsoleColor.Green,
        ConsoleColor.Cyan,
        ConsoleColor.Magenta,
    };

    private static ConsoleColor ColorForColumn(int col, int offset)
    {
        for (int i = 0; i < LetterSpans.Length; i++)
        {
            var (start, length) = LetterSpans[i];
            if (col >= start && col < start + length)
                return Rainbow[(i + offset) % Rainbow.Length];
        }

        return ConsoleColor.White;
    }


    private static void DrawBannerFrame(int top, int offset)
    {
        Console.SetCursorPosition(0, top);

        foreach (string row in Banner)
        {
            Console.Write("  ");
            for (int col = 0; col < row.Length; col++)
            {
                char c = row[col];

                if (c == ' ')
                {
                    Console.Write(' ');
                    continue;
                }

                Console.ForegroundColor = ColorForColumn(col, offset);
                Console.Write(c);
            }
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private static void Header()
    {
        int top = Console.CursorTop;


        int frames = Rainbow.Length * 2;
        for (int frame = 0; frame < frames; frame++)
        {
            DrawBannerFrame(top, frame);
            Thread.Sleep(45);
        }


        DrawBannerFrame(top, 0);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('-', Banner[0].Length));
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("  PERMISSION FIXER");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  by MyDadsSoft");
        Console.ResetColor();

        Line();
        Console.WriteLine();
    }

    private static void ShowFoundBanner()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("┌─ INSTALL DETECTED ───────────────────────────────────────");
        Console.ResetColor();

        Console.Write("│ ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("FiveM config : ");
        Console.ResetColor();
        Console.WriteLine(CitizenIni);

        Console.Write("│ ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("GTA V path   : ");
        Console.ResetColor();
        Console.WriteLine(GtaPath);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("└──────────────────────────────────────────────────────────");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Menu()
    {
        MenuItem("1", "Check GTA V permissions");
        MenuItem("2", "Fix GTA V permissions");
        MenuItem("3", "Check SirenSettings.log");
        MenuItem("4", "Install bundled ASI");
        MenuItem("5", "Run all fixes", highlight: true);
        MenuItem("6", "Scan drives again");
        MenuItem("0", "Exit");
        Console.WriteLine();
    }

    private static void MenuItem(string key, string label, bool highlight = false)
    {
        Console.Write("  [");
        Console.ForegroundColor = highlight ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.Write(key);
        Console.ResetColor();
        Console.Write("] ");

        if (highlight)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(label);
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine(label);
        }
    }

    private static void Section(string title)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"» {title}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Line()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("──────────────────────────────────────────────────────────");
        Console.ResetColor();
    }

    private static void SubText(string text)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void Muted(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }

    private static void Good(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  [OK] {text}");
        Console.ResetColor();
    }

    private static void GoodSuffix(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void DimSuffix(string text)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void Warn(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [!] {text}");
        Console.ResetColor();
    }

    private static void Fail(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  [X] {text}");
        Console.ResetColor();
    }

    private static void Spinner(string label, int frames = 10, int delayMs = 60)
    {
        char[] glyphs = { '|', '/', '-', '\\' };
        int left = Console.CursorLeft;
        int top = Console.CursorTop;

        for (int i = 0; i < frames; i++)
        {
            Console.SetCursorPosition(left, top);
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"  {glyphs[i % glyphs.Length]} {label}...");
            Console.ResetColor();
            Thread.Sleep(delayMs);
        }

        Console.SetCursorPosition(left, top);
        Console.Write(new string(' ', label.Length + 10));
        Console.SetCursorPosition(left, top);
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  Press ENTER to continue...");
        Console.ResetColor();
        Console.ReadLine();
    }
}
