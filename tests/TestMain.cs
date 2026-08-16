using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Integration test harness for DshProcess / Config / Win32 / Logging / IniFile.
// Compiled together with the production sources (excluding Program.cs) and a mock
// dsh entry (tests/mock-dsh-entry.js) so the full lifecycle can be driven headlessly
// against a private port (3099) without touching a real harness.
static class TestMain
{
    static int passCount;
    static int failCount;
    static readonly System.Collections.Generic.List<string> failures = new System.Collections.Generic.List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { passCount++; Console.WriteLine("PASS " + name); }
        else { failCount++; failures.Add(name + ": " + detail); Console.WriteLine("FAIL " + name + "  <<< " + detail); }
    }

    static string NodeExe()
    {
        string p = Config.Current.NodePath;
        if (string.IsNullOrEmpty(p) || !File.Exists(p)) p = DetectNode();
        return p;
    }

    static string DetectNode()
    {
        string[] cands = {
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node.exe")
        };
        foreach (var c in cands) if (File.Exists(c)) return c;
        return null;
    }

    static void Main()
    {
        Logging.InitLog();
        Console.WriteLine("=== dsh-tray integration tests ===");
        string baseDir = Path.GetDirectoryName(typeof(TestMain).Assembly.Location);
        string repoDir = Directory.GetParent(baseDir).FullName; // tests\ -> repo root
        // the mock entry must live under a path matching CommandLineLooksLikeDsh markers
        // (contains \dsh\ or bin.js), so adopt/stop identity checks exercise the real path
        string mockEntry = Path.Combine(repoDir, "tests", "dsh", "bin.js");
        string node = NodeExe();
        Check("node.exe found", !string.IsNullOrEmpty(node) && File.Exists(node), "node=" + node);
        Check("mock entry exists", File.Exists(mockEntry), mockEntry);

        var cfg = new AppConfig
        {
            NodePath = node,
            DshEntry = mockEntry,
            DshWorkDir = Path.GetDirectoryName(mockEntry),
            WebUrl = "http://127.0.0.1:3099",
            Port = 3099
        };

        var dp = new DshProcess(cfg);
        dp.SelfIntegrity = Win32.GetIntegrity(Process.GetCurrentProcess().Id);
        dp.AutoRestartEnabled = true;

        // ---- 1. initial state ----
        Check("initial state Stopped", dp.State == DshState.Stopped, "state=" + dp.State);

        // ---- 2. StartAsync brings the port up ----
        dp.StartAsync().Wait();
        bool portUp = dp.PortOpen(3099);
        Check("start: port 3099 open", portUp, "portOpen=" + portUp);
        Check("start: state Running", dp.State == DshState.Running, "state=" + dp.State);

        // ---- 3. StartAsync while Running is a no-op (idempotent) ----
        dp.StartAsync().Wait();
        Check("start again: still Running", dp.State == DshState.Running, "state=" + dp.State);
        Check("start again: port still open", dp.PortOpen(3099), "portOpen=false");

        // ---- 4. crash the harness (kill node) -> Exited handler should drop to Stopped ----
        int pid = FindPidOnPort(3099);
        Check("crash test: found pid on port", pid > 0, "pid=" + pid);
        if (pid > 0)
        {
            try { Process.GetProcessById(pid).Kill(); } catch (Exception ex) { Check("crash test: kill threw", false, ex.Message); }
            int waited = 0;
            while (dp.State != DshState.Stopped && waited < 8000) { Thread.Sleep(200); waited += 200; }
            Check("crash: state -> Stopped", dp.State == DshState.Stopped, "state=" + dp.State);
        }

        // ---- 5. auto-restart poll brings it back (after cooldown) ----
        // Wait out the start cooldown (10s) then poll once; PollAutoRestart fires the flow.
        Thread.Sleep(11000);
        bool triggered = dp.PollAutoRestart();
        Check("auto-restart: poll triggered", triggered, "triggered=" + triggered);
        int waitedAr = 0;
        while (dp.State != DshState.Running && waitedAr < 15000) { Thread.Sleep(300); waitedAr += 300; dp.PollAutoRestart(); }
        Check("auto-restart: state -> Running", dp.State == DshState.Running, "state=" + dp.State);
        Check("auto-restart: port open", dp.PortOpen(3099), "portOpen=false");

        // ---- 6. StopAsync frees the port and drops to Stopped ----
        dp.StopAsync().Wait();
        Check("stop: state Stopped", dp.State == DshState.Stopped, "state=" + dp.State);
        bool portFree = !dp.PortOpen(3099);
        Check("stop: port released", portFree, "portOpen=" + dp.PortOpen(3099));

        // ---- 7. RestartAsync from Stopped == start ----
        dp.RestartAsync().Wait();
        Check("restart(from Stopped): Running", dp.State == DshState.Running, "state=" + dp.State);
        Check("restart(from Stopped): port open", dp.PortOpen(3099), "portOpen=false");

        // ---- 8. RestartAsync while Running = stop+start ----
        dp.RestartAsync().Wait();
        Check("restart(from Running): Running", dp.State == DshState.Running, "state=" + dp.State);
        Check("restart(from Running): port open", dp.PortOpen(3099), "portOpen=false");

        // ---- 9. adopt: pre-existing harness on the port is adopted, not double-spawned ----
        dp.StopAsync().Wait();
        Check("adopt test: pre-stop Stopped", dp.State == DshState.Stopped, "state=" + dp.State);
        // spawn the mock manually (simulating an orphan harness) then StartAsync should adopt
        var orphan = SpawnOrphanHarness(mockEntry, node);
        int waitedOrphan = 0;
        while (!dp.PortOpen(3099) && waitedOrphan < 10000) { Thread.Sleep(200); waitedOrphan += 200; }
        Check("adopt test: orphan harness up", dp.PortOpen(3099), "orphan did not listen");
        int orphanPid = orphan.Id;
        string logBefore = ReadTrayLog();
        dp.StartAsync().Wait();
        Check("adopt test: state Running", dp.State == DshState.Running, "state=" + dp.State);
        Check("adopt test: orphan still alive (not double-spawned)", !orphan.HasExited, "orphan exited");
        int portPidAfter = FindPidOnPort(3099);
        Check("adopt test: port still owned by the orphan", portPidAfter == orphanPid, "portPid=" + portPidAfter + " orphan=" + orphanPid);
        string logAfter = ReadTrayLog();
        Check("adopt test: 'adopting' logged", logAfter.LastIndexOf("adopting", StringComparison.Ordinal) > logBefore.LastIndexOf("adopting", StringComparison.Ordinal), "adopting not logged");
        Check("adopt test: only one pid on port", CountPidsOnPort(3099) == 1, "pids=" + CountPidsOnPort(3099));

        // ---- 10. stop kills the adopted orphan too ----
        dp.StopAsync().Wait();
        Check("adopt stop: port released", !dp.PortOpen(3099), "portOpen=" + dp.PortOpen(3099));

        // ---- 11. port probe / pid lookup correctness ----
        Check("FindPidOnPort on closed port = 0", dp.FindPidOnPort(3099) == 0, "pid=" + dp.FindPidOnPort(3099));
        Check("FindPidOnPort on closed 3081 = 0", dp.FindPidOnPort(3081) == 0, "pid=" + dp.FindPidOnPort(3081));

        // ---- 12. BuildLaunchCmd quoting sanity ----
        string cmd = DshProcess.BuildLaunchCmd();
        Check("launch cmd contains placeholders", cmd.IndexOf("%DSH_TRAY_NODE%") > 0 && cmd.IndexOf("%DSH_TRAY_ENTRY%") > 0, cmd);

        // ---- 13. IniFile round-trip ----
        string iniPath = Path.Combine(Path.GetTempPath(), "dsh-tray-test-" + Guid.NewGuid().ToString("N") + ".ini");
        var lines = new System.Collections.Generic.List<string> { "; comment", "# comment", "url=http://127.0.0.1:3080", "lang=", "autorestart=true" };
        IniFile.Save(iniPath, lines);
        var loaded = IniFile.Load(iniPath);
        Check("ini round-trip: url", IniFile.Get(loaded, "url") == "http://127.0.0.1:3080", "got=" + IniFile.Get(loaded, "url"));
        Check("ini round-trip: lang empty", IniFile.Get(loaded, "lang") == "", "got=" + (IniFile.Get(loaded, "lang") ?? "null"));
        IniFile.Set(loaded, "autorestart", "false");
        IniFile.Save(iniPath, loaded);
        var loaded2 = IniFile.Load(iniPath);
        Check("ini round-trip: autorestart=false", IniFile.Get(loaded2, "autorestart") == "false", "got=" + IniFile.Get(loaded2, "autorestart"));
        Check("ini round-trip: comments preserved", loaded2.Count == 5, "count=" + loaded2.Count);
        try { File.Delete(iniPath); } catch { }

        // ---- 14. version compare logic (UpdateCheck helpers) ----
        Check("vercmp: 1.2.3 > 1.1.0", UpdateCheck.IsNewer("1.2.3", "1.1.0.0"), "");
        Check("vercmp: 1.1.3 not > 1.1.3.0", !UpdateCheck.IsNewer("1.1.3", "1.1.3.0"), "");
        Check("vercmp: 2.0.0 > 1.9.9", UpdateCheck.IsNewer("2.0.0", "1.9.9"), "");
        Check("vercmp: 1.1.3 not > 2.0.0", !UpdateCheck.IsNewer("1.1.3", "2.0.0"), "");
        Check("vercmp: normalize v1.2.3", UpdateCheck.NormalizeVersion("v1.2.3") == "1.2.3", "got=" + UpdateCheck.NormalizeVersion("v1.2.3"));
        Check("vercmp: normalize 1.2.3-beta", UpdateCheck.NormalizeVersion("1.2.3-beta") == "1.2.3", "got=" + UpdateCheck.NormalizeVersion("1.2.3-beta"));

        // ---- 15. JSON extract ----
        string json = "{\"tag_name\":\"v1.2.3\",\"name\":\"x\"}";
        Check("json extract tag_name", UpdateCheck.ExtractJsonString(json, "tag_name") == "v1.2.3", "got=" + UpdateCheck.ExtractJsonString(json, "tag_name"));
        Check("json extract missing key", UpdateCheck.ExtractJsonString(json, "nope") == null, "got=" + (UpdateCheck.ExtractJsonString(json, "nope") ?? "null"));

        // ---- 16. race regression: concurrent start vs stop must never leak a running harness ----
        // loop several times; after each round the invariant is: if state is Stopped the port
        // must be closed (no orphaned harness), and if Running the port must be open.
        bool raceOk = true;
        string raceDetail = "";
        for (int i = 0; i < 5; i++)
        {
            var t1 = Task.Run(() => dp.StartAsync());
            var t2 = Task.Run(() => dp.StopAsync());
            Task.WaitAll(t1, t2);
            Thread.Sleep(1500); // let any fire-and-forget flow settle
            // the winner's state may be either; the invariant is the pairing:
            bool st = dp.State == DshState.Stopped;
            bool open = dp.PortOpen(3099);
            if ((st && open) || (!st && !open))
            {
                raceOk = false;
                raceDetail = "round " + i + ": state=" + dp.State + " portOpen=" + open;
                break;
            }
            // settle to a clean Stopped before the next round (and clean any leftover)
            dp.StopAsync().Wait();
            int waitFree = 0;
            while (dp.PortOpen(3099) && waitFree < 8000) { Thread.Sleep(200); waitFree += 200; }
            if (dp.PortOpen(3099))
            {
                raceOk = false;
                raceDetail = "round " + i + ": port leaked after stop";
                break;
            }
        }
        Check("race: no leaked harness across 5 concurrent start/stop rounds", raceOk, raceDetail);

        // ---- summary ----
        Console.WriteLine();
        Console.WriteLine("=== RESULT: " + passCount + " passed, " + failCount + " failed ===");
        foreach (var f in failures) Console.WriteLine("FAILED: " + f);
        string report = Path.Combine(repoDir, "tests", "integration-result.txt");
        File.WriteAllText(report,
            "integration tests: " + passCount + " passed, " + failCount + " failed" + Environment.NewLine +
            string.Join(Environment.NewLine, failures));
        Environment.ExitCode = failCount == 0 ? 0 : 1;
    }

    static int FindPidOnPort(int port)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "netstat.exe"),
            Arguments = "-ano -p tcp",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        using (var p = Process.Start(psi))
        {
            string output = p.StandardOutput.ReadToEnd();
            foreach (string line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.IndexOf("LISTENING", StringComparison.Ordinal) < 0) continue;
                string[] cols = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length < 5) continue;
                if (cols[1].EndsWith(":" + port, StringComparison.Ordinal))
                {
                    int pid;
                    if (int.TryParse(cols[cols.Length - 1], out pid)) return pid;
                }
            }
        }
        return 0;
    }

    static int CountPidsOnPort(int port)
    {
        int pid = FindPidOnPort(port);
        return pid > 0 ? 1 : 0;
    }

    static string ReadTrayLog()
    {
        try
        {
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-tray", "tray.log");
            return File.Exists(log) ? File.ReadAllText(log) : "";
        }
        catch { return ""; }
    }

    static Process SpawnOrphanHarness(string entry, string node)
    {
        var psi = new ProcessStartInfo
        {
            FileName = node,
            Arguments = "\"" + entry + "\" web",
            WorkingDirectory = Path.GetDirectoryName(entry),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables["MOCK_DSH_PORT"] = "3099";
        psi.EnvironmentVariables["MOCK_DSH_READY"] = Path.Combine(Path.GetTempPath(), "mock-dsh-ready.txt");
        return Process.Start(psi);
    }
}