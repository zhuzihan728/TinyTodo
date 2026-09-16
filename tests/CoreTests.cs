using System;
using System.IO;
using System.Linq;
using TinyTodo;

internal static class CoreTests
{
    private static int count;
    private static void Assert(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, name);
    }
    private static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TinyTodo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "tasks.json");
            var store = new Store(path); store.Load();
            Assert(store.Current.Tasks.Count == 0, "empty first run");
            var a = new Todo { Name = "A 中文", Description = "line 1\nline 2" };
            var b = new Todo { Name = "B", Due = "2026-09-14" };
            var c = new Todo { Name = "C" };
            b.Prerequisites.Add(a.Id); c.Prerequisites.Add(b.Id);
            store.Change(s => s.Tasks.AddRange(new Todo[] { a, b, c }));
            Reject(() => store.Change(s => Rules.Complete(s, b.Id)), "blocked completion");
            Assert(!Rules.Get(store.Current, b.Id).Done, "failed change leaves state intact");
            Reject(() => store.Change(s => Rules.Get(s, a.Id).Prerequisites.Add(c.Id)), "indirect cycle rejected");
            Reject(() => store.Change(s => Rules.Get(s, a.Id).Prerequisites.Add(a.Id)), "self dependency rejected");
            Reject(() => store.Change(s => Rules.Get(s, a.Id).Prerequisites.Add("missing")), "missing dependency rejected");
            Reject(() => store.Change(s => Rules.Get(s, b.Id).Prerequisites.Add(a.Id)), "duplicate dependency rejected");
            Reject(() => store.Change(s => Rules.Get(s, a.Id).Name = "  "), "empty name rejected");
            Reject(() => store.Change(s => Rules.Get(s, a.Id).Due = "2026-02-30"), "invalid date rejected");
            store.Change(s => Rules.Complete(s, a.Id));
            Assert(Rules.Blockers(store.Current, Rules.Get(store.Current, b.Id)).Count == 0, "completion unlocks next task");
            store.Change(s => Rules.Complete(s, b.Id));
            Reject(() => store.Change(s => Rules.Restore(s, a.Id)), "restore cannot invalidate completed dependent");
            store.Change(s => Rules.Restore(s, b.Id));
            Assert(Rules.Blockers(store.Current, Rules.Get(store.Current, c.Id)).Count == 1, "restore blocks pending successor");
            store.Change(s => Rules.DeleteHistory(s, a.Id));
            Assert(!Rules.Get(store.Current, b.Id).Prerequisites.Any(), "history deletion removes edges");
            Assert(Rules.Blockers(store.Current, Rules.Get(store.Current, b.Id)).Count == 0, "deleting completed prerequisite keeps task actionable");
            Reject(() => store.Change(s => Rules.DeleteHistory(s, b.Id)), "active task cannot be deleted as history");
            var d = new Todo { Name = "D 中文", Description = "first\nsecond \"quoted\"" };
            var e = new Todo { Name = "E" };
            e.Prerequisites.Add(b.Id); e.Prerequisites.Add(d.Id);
            store.Change(s => s.Tasks.AddRange(new Todo[] { d, e }));
            store.Change(s => Rules.Complete(s, b.Id));
            Assert(Rules.Blockers(store.Current, Rules.Get(store.Current, e.Id)).Count == 1, "all prerequisites required");
            var reloaded = new Store(path); reloaded.Load();
            Assert(Rules.Get(reloaded.Current, d.Id).Description == d.Description, "UTF-8 multiline JSON round trip");
            Assert(Rules.Get(reloaded.Current, b.Id).Due == b.Due && Rules.Get(reloaded.Current, d.Id).Due == null, "optional dates persist");
            Assert(File.Exists(path + ".bak"), "backup exists");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool failed = false;
                try { store.Change(s => Rules.Get(s, d.Id).Name = "changed"); }
                catch (IOException) { failed = true; }
                Assert(failed && Rules.Get(store.Current, d.Id).Name == d.Name, "failed disk write rolls back memory");
            }
            File.WriteAllText(path, "{bad json");
            bool damaged = false;
            try { new Store(path).Load(); } catch { damaged = true; }
            Assert(damaged && File.ReadAllText(path) == "{bad json", "corrupt input is not overwritten");
            store.RecoverBackup();
            Rules.Validate(store.Current);
            Assert(Directory.GetFiles(dir, "*.damaged-*").Length == 1, "recovery preserves damaged original");
            Assert(Store.Read(path).Tasks.Count > 0, "backup recovery is readable");
            var graph = new State();
            var p = new Todo { Name = "root" }; var q = new Todo { Name = "left" };
            var r = new Todo { Name = "right" }; var z = new Todo { Name = "shared child" };
            var isolated = new Todo { Name = "isolated" };
            q.Prerequisites.Add(p.Id); r.Prerequisites.Add(p.Id);
            z.Prerequisites.Add(q.Id); z.Prerequisites.Add(r.Id);
            graph.Tasks.AddRange(new Todo[] { p, q, r, z, isolated });
            Rules.Validate(graph);
            var ancestors = Rules.Related(graph, z.Id, true);
            Assert(ancestors.Count == 3, "preview includes all transitive prerequisites");
            Assert(ancestors[p.Id] == 2 && ancestors[q.Id] == 1 && ancestors[r.Id] == 1, "preview distinguishes direct and indirect links");
            var descendants = Rules.Related(graph, p.Id, false);
            Assert(descendants.Count == 3 && descendants[z.Id] == 2, "shared descendant appears once in list");
            Assert(!descendants.ContainsKey(p.Id), "preview excludes current task from related rows");
            Assert(Rules.Related(graph, q.Id, false).Count == 1, "unrelated sibling is not a child");
            Assert(Rules.Related(graph, isolated.Id, true).Count == 0 && Rules.Related(graph, isolated.Id, false).Count == 0, "isolated preview has no links");
            Rules.Complete(graph, p.Id); Rules.Complete(graph, q.Id); Rules.Complete(graph, r.Id); Rules.Complete(graph, z.Id);
            Assert(Rules.Related(graph, p.Id, false).Count == 3, "history tasks remain visible in relationship preview");
            string legacy = Path.Combine(dir, "legacy.json");
            File.WriteAllText(legacy, "{\"Version\":1,\"Tasks\":[],\"Window\":{\"Floating\":true,\"X\":12,\"Y\":34,\"HasPosition\":true}}");
            State old = Store.Read(legacy);
            Assert(old.Window.Floating && old.Window.X == 12 && !old.Window.HasIconPosition, "v1 settings load with new icon defaults");
            Assert(old.Window.CatMode == CatVisibilityMode.HideFullscreen && old.Window.CatBlacklist.Count == 0, "old task files default to same-screen fullscreen hiding with empty blacklist");
            Assert(!CatVisibility.ShouldShow(CatVisibilityMode.Hidden, null, false, false), "hidden default keeps cat hidden");
            Assert(CatVisibility.ShouldShow(CatVisibilityMode.HideFullscreen, null, false, false), "ordinary apps keep default cat visible");
            Assert(!CatVisibility.ShouldShow(CatVisibilityMode.HideFullscreen, null, true, false), "same-screen fullscreen hides default cat");
            Assert(CatVisibility.ShouldShow(CatVisibilityMode.Always, null, true, false), "always-on setting includes fullscreen");
            Assert(CatVisibility.ShouldShow(CatVisibilityMode.Hidden, true, true, false), "manual summon overrides default during this session");
            Assert(!CatVisibility.ShouldShow(CatVisibilityMode.Always, false, false, false), "manual hide overrides always-on during this session");
            foreach (CatVisibilityMode mode in Enum.GetValues(typeof(CatVisibilityMode)))
                foreach (bool? manual in new bool?[] { null, false, true })
                    Assert(!CatVisibility.ShouldShow(mode, manual, false, true) && !CatVisibility.ShouldShow(mode, manual, true, true), "blacklist overrides mode and manual choice: " + mode + "/" + manual);
            var blacklist = CatVisibility.ParseBlacklist("Code.exe\r\ncode.EXE\n C:\\Games\\Wow.exe \nLeague of Legends.exe\n");
            Assert(blacklist.SequenceEqual(new string[] { "Code", "Wow", "League of Legends" }), "blacklist normalizes paths, suffixes, whitespace and duplicates");
            Assert(CatVisibility.IsBlocked(blacklist, new string[] { "WOW", "other" }) && !CatVisibility.IsBlocked(blacklist, new string[] { "CodeHelper" }), "blacklist uses exact case-insensitive process names");
            Reject(() => CatVisibility.ParseBlacklist("*.exe"), "blacklist rejects wildcards");
            store.Change(s => { s.Window.CatMode = CatVisibilityMode.Hidden; s.Window.CatBlacklist = blacklist; });
            State savedCat = Store.Read(path);
            Assert(savedCat.Window.CatMode == CatVisibilityMode.Hidden && savedCat.Window.CatBlacklist.SequenceEqual(blacklist) && savedCat.Tasks.Count == store.Current.Tasks.Count, "floating preferences persist without changing task records");
            var locations = new DataLocations(Path.Combine(dir, "metadata")); locations.Load();
            var moving = new Store(locations.TaskPath); moving.Load();
            moving.Change(s => s.Tasks.Add(new Todo { Name = "keep through migration" }));
            string original = moving.PathName;
            locations.MoveTo(moving, Path.Combine(dir, "chosen"));
            Assert(Store.Read(moving.PathName).Tasks[0].Name == "keep through migration", "migration preserves existing tasks");
            Assert(!File.Exists(original) && !DataLocations.TaskFiles(Path.GetDirectoryName(original)).Any(), "successful migration removes original task copies");
            moving.Change(s => s.Tasks[0].Description = "saved in new location");
            var locatorAgain = new DataLocations(locations.MetadataDirectory); locatorAgain.Load();
            Assert(Store.Read(locatorAgain.TaskPath).Tasks[0].Description == "saved in new location", "restart resolves migrated data location");
            string conflict = Path.Combine(dir, "conflict", "TinyTodo-Data"); Directory.CreateDirectory(conflict);
            File.WriteAllText(Path.Combine(conflict, "unrelated.txt"), "do not touch");
            Reject(() => locations.MoveTo(moving, Path.GetDirectoryName(conflict)), "migration refuses an unowned nonempty folder");
            string unrelated = Path.Combine(Path.GetDirectoryName(moving.PathName), "personal-notes.txt"); File.WriteAllText(unrelated, "keep");
            locations.RemoveOwnedData();
            Assert(!File.Exists(moving.PathName) && !File.Exists(Path.Combine(locations.MetadataDirectory, "data-locations.json")), "uninstall cleanup removes tracked data and locator");
            Assert(File.ReadAllText(unrelated) == "keep" && File.Exists(Path.Combine(conflict, "unrelated.txt")), "uninstall preserves unrelated files and parent directories");
            var linkStore = new Store(Path.Combine(dir, "links.json")); linkStore.Load();
            var root = new Todo { Name = "anchor" }; var child = new Todo { Name = "child" }; child.Prerequisites.Add(root.Id);
            linkStore.Change(s => { s.Tasks.Add(root); s.Tasks.Add(child); Rules.Complete(s, root.Id); Rules.Complete(s, child.Id); });
            var newBefore = new Todo { Name = "new prerequisite" };
            linkStore.Change(s => Rules.AddRelated(s, newBefore, root.Id, true));
            Assert(!Rules.Get(linkStore.Current, root.Id).Done && !Rules.Get(linkStore.Current, child.Id).Done, "new prerequisite restores affected completed chain atomically");
            Assert(Rules.Get(linkStore.Current, root.Id).Prerequisites.Contains(newBefore.Id), "quick prerequisite links new task to anchor");
            var newAfter = new Todo { Name = "new successor" };
            linkStore.Change(s => Rules.AddRelated(s, newAfter, root.Id, false));
            Assert(Rules.Get(linkStore.Current, newAfter.Id).Prerequisites.Contains(root.Id), "quick successor links anchor automatically");
            int savedCount = linkStore.Current.Tasks.Count;
            var invalid = new Todo { Name = "cycle" }; invalid.Prerequisites.Add(child.Id);
            Reject(() => linkStore.Change(s => Rules.AddRelated(s, invalid, root.Id, true)), "quick add rejects cycle through existing descendant");
            Assert(linkStore.Current.Tasks.Count == savedCount && !Rules.Get(linkStore.Current, root.Id).Prerequisites.Contains(invalid.Id), "failed quick add rolls back task and relationship");
            linkStore.Change(s => Rules.Delete(s, root.Id));
            Assert(!linkStore.Current.Tasks.Any(t => t.Id == root.Id), "pending tasks can be permanently deleted");
            Assert(!Rules.Get(linkStore.Current, child.Id).Prerequisites.Any() && !Rules.Get(linkStore.Current, newAfter.Id).Prerequisites.Any(), "pending deletion removes every incoming reference");
            var compatible = new Store(linkStore.PathName); compatible.Load();
            Assert(compatible.Current.Version == 1 && compatible.Current.Tasks.Count == savedCount - 1, "v3 keeps version-one data schema and persists new operations");
            Assert(!old.Window.DateCountdown && !old.Window.DateDescending && old.Window.ClockZoneId == null, "legacy settings default to ascending dates and system timezone");
            var dueEarly = new Todo { Name = "early", Due = "2026-09-15" };
            var dueLate = new Todo { Name = "late", Due = "2026-09-30" };
            var noDate = new Todo { Name = "none" };
            var dueItems = new Todo[] { noDate, dueLate, dueEarly };
            Assert(Rules.ByDate(dueItems, false).SequenceEqual(new Todo[] { dueEarly, dueLate, noDate }), "ascending date order puts undated last");
            Assert(Rules.ByDate(dueItems, true).SequenceEqual(new Todo[] { dueLate, dueEarly, noDate }), "descending date order still puts undated last");
            Assert(Rules.Countdown("2026-09-15", new DateTime(2026, 9, 15, 23, 59, 59)) == "今天", "countdown compares calendar dates, not remaining hours");
            Assert(Rules.Countdown("2026-09-16", new DateTime(2026, 9, 15)) == "1 天后", "future day countdown");
            Assert(Rules.Countdown("2026-09-14", new DateTime(2026, 9, 15)) == "已过 1 天", "past day countdown");
            Assert(Rules.Countdown("2028-03-01", new DateTime(2028, 2, 28)) == "2 天后", "leap day is included");
            Assert(Rules.Countdown(null, DateTime.Today) == "—", "undated task has no countdown");
            var springBefore = new DateTimeOffset(2026, 3, 29, 0, 59, 59, TimeSpan.Zero);
            var springAfter = springBefore.AddSeconds(1);
            Assert(WorldClock.At(springBefore, WorldClock.UK).Offset == TimeSpan.Zero && WorldClock.At(springAfter, WorldClock.UK).Hour == 2 && WorldClock.At(springAfter, WorldClock.UK).Offset == TimeSpan.FromHours(1), "UK spring transition uses BST at exact UTC boundary");
            var autumnBefore = new DateTimeOffset(2026, 10, 25, 0, 59, 59, TimeSpan.Zero);
            var autumnAfter = autumnBefore.AddSeconds(1);
            Assert(WorldClock.At(autumnBefore, WorldClock.UK).Offset == TimeSpan.FromHours(1) && WorldClock.At(autumnAfter, WorldClock.UK).Hour == 1 && WorldClock.At(autumnAfter, WorldClock.UK).Offset == TimeSpan.Zero, "UK autumn transition restores GMT without ambiguous input");
            Assert(WorldClock.At(springBefore, WorldClock.China).Offset == TimeSpan.FromHours(8) && WorldClock.At(autumnAfter, WorldClock.China).Offset == TimeSpan.FromHours(8), "China remains UTC+8 in both seasons");
            var noonUtc = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
            Assert(WorldClock.At(noonUtc.AddSeconds(-1), WorldClock.AoE).Date == new DateTime(2026, 9, 14) && WorldClock.At(noonUtc, WorldClock.AoE).Date == new DateTime(2026, 9, 15), "AoE date rolls at noon UTC");
            var localClock = TimeZoneInfo.ConvertTime(noonUtc, TimeZoneInfo.Local);
            Assert(WorldClock.At(noonUtc, null).DateTime == localClock.DateTime && WorldClock.At(noonUtc, null).Offset == localClock.Offset, "default clock uses system timezone");
            Assert(WorldClock.Display(WorldClock.At(autumnBefore, WorldClock.UK)).EndsWith("UTC+01:00") && WorldClock.Display(WorldClock.At(autumnAfter, WorldClock.UK)).EndsWith("UTC+00:00"), "clock displays effective DST offset");
            string featuresPath = Path.Combine(dir, "features.json");
            var featureStore = new Store(featuresPath); featureStore.Load();
            featureStore.Change(s => { s.Tasks.Add(dueEarly); s.Window.DateCountdown = true; s.Window.DateDescending = true; s.Window.ClockZoneId = WorldClock.UK; Rules.Get(s, dueEarly.Id).Important = true; });
            var savedFeatures = Store.Read(featuresPath);
            Assert(savedFeatures.Tasks[0].Important && savedFeatures.Tasks[0].Name == "early", "important flag persists without changing task name");
            Assert(savedFeatures.Window.DateCountdown && savedFeatures.Window.DateDescending && savedFeatures.Window.ClockZoneId == WorldClock.UK, "date and timezone preferences persist");
            using (var lockedFeatures = new FileStream(featuresPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool failedFlag = false;
                try { featureStore.Change(s => s.Tasks[0].Important = false); } catch (IOException) { failedFlag = true; }
                Assert(failedFlag && featureStore.Current.Tasks[0].Important, "failed important write rolls back state");
            }
            var markdown = SimpleMarkdown.Parse("# Heading\n- **bold** and *italic*\n> quote\n`inline`\n```cs\n**literal**\n```\n[site](https://example.com)\n![alt](missing.png)");
            Assert(markdown[0].Heading == 1 && markdown[0].Runs[0].Text == "Heading", "Markdown heading");
            Assert(markdown[1].Prefix == "• " && markdown[1].Runs.Any(x => x.Bold && x.Text == "bold") && markdown[1].Runs.Any(x => x.Italic && x.Text == "italic"), "Markdown list and inline emphasis");
            Assert(markdown[2].Quote && markdown[3].Runs[0].Code && markdown[4].Code && markdown[4].Runs[0].Text == "**literal**", "Markdown quote and literal code");
            Assert(markdown[5].Runs[0].Text == "site" && markdown[5].Runs[0].Link == "https://example.com" && markdown[6].Runs[0].Text == "alt", "Markdown link labels retain URL targets and images remain alt text");
            Assert(SimpleMarkdown.Parse("[wiki](https://example.com/Function_(math))")[0].Runs[0].Link == "https://example.com/Function_(math)", "balanced URL parentheses");
            Assert(SimpleMarkdown.Parse("https://example.com/test.")[0].Runs[0].Link == "https://example.com/test", "bare URL excludes prose punctuation");
            Assert(SimpleMarkdown.Parse("[unsafe](file:///C:/test.exe)")[0].Runs[0].Link == null && !SimpleMarkdown.IsWebLink("javascript:alert(1)"), "only web URLs can be launched");
            Assert(SimpleMarkdown.Parse("`https://example.com`")[0].Runs[0].Link == null, "inline code is not a link");
            Assert(SimpleMarkdown.Parse("- [x] done")[0].Prefix == "☑ " && SimpleMarkdown.Parse("~~old~~")[0].Runs[0].Strike, "task list and strike formatting");
            Assert(SimpleMarkdown.Parse("**unfinished")[0].Runs[0].Text == "**unfinished", "incomplete Markdown remains readable");
            Assert(SimpleMarkdown.Parse("<script>alert(1)</script>")[0].Runs[0].Text == "<script>alert(1)</script>", "HTML is displayed as text");
            Console.WriteLine("\nAll " + count + " assertions passed. UI checks are manual (see README).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
