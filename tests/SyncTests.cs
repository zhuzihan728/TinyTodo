using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TinyTodo;

internal static class SyncTests
{
    private static int count;
    private static void Assert(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
    private static void Reject(Action action, string message, bool conflict = false)
    {
        try { action(); } catch (SyncException e) { Assert(!conflict || e.Conflict, message); return; }
        throw new Exception("FAIL: " + message);
    }
    internal sealed class MemoryRemote : ISyncRemote
    {
        internal SyncData Data = new SyncData();
        internal string Sha;
        internal int Writes, Reads;
        internal Action BeforeRead;
        internal Action BeforeWrite;
        public RemoteData Read() { Reads++; if (BeforeRead != null) BeforeRead(); return new RemoteData { Sha = Sha, Data = SyncData.Decode(Data.Encode()) }; }
        public void Write(SyncData data, string sha)
        {
            if (BeforeWrite != null) BeforeWrite();
            if (sha != Sha) throw new SyncException("Remote changed");
            Data = SyncData.Decode(data.Encode()); Sha = (++Writes).ToString();
        }
    }
    private static SyncData Copy(SyncData data) { return SyncData.Decode(data.Encode()); }
    internal static void Run(string folder)
    {
        Assert(GitHubLogin.Repository("https://github.com/example/tasks.git/") == "https://github.com/example/tasks", "GitHub repository links normalize without shell commands");
        foreach (string url in new[] { "http://github.com/example/tasks", "https://github.com.evil.test/example/tasks", "https://token@github.com/example/tasks", "https://github.com/example/tasks/tree/main", "https://github.com/example/tasks?token=secret" })
            Reject(() => GitHubLogin.Repository(url), "reject invalid repository authority or path");
        Reject(() => GitHubLogin.Variable("github_pat_do_not_store_me"), "token cannot be pasted as an environment variable name");
        string env = "TINYTODO_TEST_TOKEN_" + Guid.NewGuid().ToString("N");
        Reject(() => GitHubLogin.Token(env), "missing explicit token variable does not silently use another identity");
        Environment.SetEnvironmentVariable(env, "local-test-value");
        try { Assert(GitHubLogin.Token(env) == "local-test-value", "explicit token reads process environment"); }
        finally { Environment.SetEnvironmentVariable(env, null); }
        var task = new Todo { Name = "Sync task", Description = "original", Important = false };
        var state = new State(); state.Tasks.Add(task); state.Window.X = 123; state.Window.SyncRepository = "https://github.com/example/tasks"; state.Window.SyncTokenVariable = "PRIVATE_ENV_NAME";
        var original = SyncData.Capture(state);
        Assert(!original.Encode().Contains("Window") && !original.Encode().Contains("PRIVATE_ENV_NAME") && !original.Encode().Contains("SyncRepository"), "wire data excludes device settings and credential configuration");
        Assert(Copy(original).Same(original), "sync document round trips without changes");
        Reject(() => SyncData.Decode("{\"Version\":9,\"Tasks\":[],\"FocusSessions\":[]}"), "unsupported remote schema is rejected");
        Reject(() => SyncData.Decode("{\"Version\":1,\"Tasks\":[null],\"FocusSessions\":[]}"), "invalid remote task is rejected before applying");
        Reject(() => SyncData.Decode("not json"), "invalid remote JSON is rejected");
        Reject(() => SyncData.Decode("{}"), "incomplete remote document cannot be treated as an empty task list");
        var left = Copy(original); var right = Copy(original); left.Tasks[0].Name = "Renamed"; right.Tasks[0].Important = true;
        var combined = SyncData.Merge(original, left, right);
        Assert(combined.Tasks[0].Name == "Renamed" && combined.Tasks[0].Important, "different fields of one task merge without losing either edit");
        Assert(original.Tasks[0].Name == "Sync task" && !original.Tasks[0].Important, "merging does not mutate baseline");
        right.Tasks[0].Name = "Other rename";
        Reject(() => SyncData.Merge(original, left, right), "same-field edits require conflict resolution", true);
        right = Copy(original); right.Tasks.Clear();
        Assert(SyncData.Merge(original, original, right).Tasks.Count == 0, "remote deletion propagates when local task is unchanged");
        Reject(() => SyncData.Merge(original, left, right), "delete versus edit preserves both versions as a conflict", true);
        left = Copy(original); right = Copy(original); left.Tasks.Add(new Todo { Name = "Local addition" }); right.Tasks.Add(new Todo { Name = "Remote addition" });
        Assert(SyncData.Merge(original, left, right).Tasks.Count == 3, "concurrent additions merge by task ID");
        var newDevice = SyncData.Merge(new SyncData(), new SyncData(), right);
        Assert(newDevice.Same(Copy(right)), "new empty device imports remote tasks");
        var session = new FocusSession { Id = "run:day", TaskId = task.Id, SeriesId = task.Id, StartedAt = DateTimeOffset.Now.ToString("o"), Day = DateTime.Today.ToString("yyyy-MM-dd"), Seconds = 15 };
        original.FocusSessions.Add(session); left = Copy(original); right = Copy(original);
        left.FocusSessions[0].Seconds = 30; right.FocusSessions[0].Seconds = 45;
        Assert(SyncData.Merge(original, left, right).FocusSessions.Single().Seconds == 45, "same focus checkpoint takes the larger duration without double counting");
        right.FocusSessions.Add(new FocusSession { Id = "other:day", TaskId = task.Id, SeriesId = task.Id, StartedAt = session.StartedAt, Day = session.Day, Seconds = 60 });
        Assert(SyncData.Merge(original, left, right).FocusSessions.Sum(s => s.Seconds) == 105, "independent focus sessions accumulate across devices");
        var remote = new MemoryRemote();
        var pushed = SyncExchange.Run(remote, new SyncData(), original, SyncAction.Push);
        Assert(remote.Writes == 1 && remote.Data.Same(original) && pushed.Baseline.Same(original), "push initializes an empty repository document");
        SyncExchange.Run(remote, original, original, SyncAction.Push);
        Assert(remote.Writes == 1, "unchanged push creates no duplicate commit");
        remote.Data.Tasks[0].Description = "remote description"; left = Copy(original); left.Tasks[0].Important = true;
        var pulled = SyncExchange.Run(remote, original, left, SyncAction.Pull);
        Assert(pulled.Data.Tasks[0].Important && pulled.Data.Tasks[0].Description == "remote description" && remote.Writes == 1, "pull merges local unsent edits without writing remote");
        Assert(pulled.Baseline.Same(remote.Data), "pull baseline records remote state, retaining local pending changes");
        var caughtUp = SyncExchange.Run(remote, pulled.Baseline, pulled.Data, SyncAction.Push);
        Assert(remote.Data.Same(caughtUp.Data) && remote.Writes == 2, "merged local edits push after pull");
        var during = Copy(left); during.Tasks.Add(new Todo { Name = "Edited during network request" });
        var applied = SyncData.Merge(left, during, pulled.Data);
        Assert(applied.Tasks.Count == 2 && applied.Tasks.Any(t => t.Name == "Edited during network request"), "edits made during a network operation survive completion");
        remote.BeforeWrite = delegate { remote.Sha = "concurrent-write"; };
        left = Copy(remote.Data); left.Tasks[0].Name = "Next";
        Reject(() => SyncExchange.Run(remote, remote.Data, left, SyncAction.Push), "concurrent remote write rejects stale SHA");
        Assert(remote.Data.Tasks[0].Name != "Next", "stale write does not overwrite remote tasks");
        remote.BeforeWrite = null;
        var localChoice = SyncExchange.Run(remote, original, left, SyncAction.KeepLocal);
        Assert(remote.Data.Same(left), "explicit local resolution writes selected snapshot");
        var remoteChoice = SyncExchange.Run(remote, original, original, SyncAction.KeepRemote);
        Assert(remoteChoice.Data.Same(remote.Data), "explicit remote resolution selects remote snapshot");
        var cache = Path.Combine(folder, "sync-state.json"); SyncCache.Save(cache, "https://github.com/example/tasks", original);
        Assert(SyncCache.Load(cache, "https://github.com/example/tasks").Same(original), "sync baseline persists across restarts");
        Assert(SyncCache.Load(cache, "https://github.com/example/other").Tasks.Count == 0, "switching repositories does not reuse another baseline");
        SyncCache.Save(cache, "https://github.com/example/tasks", left);
        Assert(File.Exists(cache + ".bak") && SyncCache.Load(cache, "https://github.com/example/tasks").Same(left), "baseline update is durable and backed up");
        File.WriteAllText(cache, "broken"); Reject(() => SyncCache.Load(cache, "https://github.com/example/tasks"), "corrupt baseline is reported instead of resetting sync history");
        remote = new MemoryRemote();
        Reject(() => SyncExchange.Run(remote, original, original, SyncAction.Pull), "missing previously synchronized file cannot silently erase local tasks", true);
        Console.WriteLine("All " + count + " sync assertions passed.");
    }
    private static int Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "TinyTodo-sync-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Run(folder);
            if (args.Length == 2)
            {
                var api = new GitHubData(args[0], GitHubLogin.Token(""), args[1]);
                var current = api.Read(); if (current.Sha != null) throw new Exception("Live test branch already contains sync data");
                var state = new State(); var task = new Todo { Name = "TinyTodo isolated sync check", Repeat = new RepeatRule { Kind = RepeatKind.Daily } }; state.Tasks.Add(task);
                var snapshot = SyncData.Capture(state); SyncExchange.Run(api, new SyncData(), snapshot, SyncAction.Push);
                Assert(api.Read().Data.Same(snapshot), "live GitHub push and pull preserve exact isolated task document");
                var deviceA = Copy(snapshot); var deviceB = Copy(snapshot); deviceA.Tasks[0].Important = true; deviceB.Tasks.Add(new Todo { Name = "Second isolated device" });
                SyncExchange.Run(api, snapshot, deviceA, SyncAction.Push);
                var merged = SyncExchange.Run(api, snapshot, deviceB, SyncAction.Push);
                Assert(merged.Data.Tasks.Count == 2 && merged.Data.Tasks.Single(t => t.Id == task.Id).Important, "live two-device edits merge and persist");
                var stale = api.Read(); var newer = Copy(stale.Data); newer.Tasks[0].Description = "concurrent change"; api.Write(newer, stale.Sha);
                Reject(() => api.Write(stale.Data, stale.Sha), "live GitHub rejects stale revision write");
                var fresh = api.Read(); Assert(fresh.Data.Same(newer), "failed stale upload leaves newer cloud data intact");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { Directory.Delete(folder, true); }
    }
}
