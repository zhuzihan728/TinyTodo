using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TinyTodo
{
    public sealed class Todo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Due { get; set; }
        public bool Important { get; set; }
        public List<string> Prerequisites { get; set; }
        public string CreatedAt { get; set; }
        public string CompletedAt { get; set; }
        public Todo()
        {
            Id = Guid.NewGuid().ToString("N"); Name = ""; Description = "";
            Prerequisites = new List<string>(); CreatedAt = DateTime.UtcNow.ToString("o");
        }
        [ScriptIgnore] public bool Done { get { return CompletedAt != null; } }
    }

    public sealed class Settings
    {
        public bool Floating { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool HasPosition { get; set; }
        public int IconX { get; set; }
        public int IconY { get; set; }
        public bool HasIconPosition { get; set; }
        public bool DateCountdown { get; set; }
        public bool DateDescending { get; set; }
        public string ClockZoneId { get; set; }
    }

    public sealed class State
    {
        public int Version { get; set; }
        public List<Todo> Tasks { get; set; }
        public Settings Window { get; set; }
        public State() { Version = 1; Tasks = new List<Todo>(); Window = new Settings(); }
    }

    public static class Rules
    {
        public static IEnumerable<Todo> ByDate(IEnumerable<Todo> tasks, bool descending)
        {
            var dated = tasks.OrderBy(t => t.Due == null);
            return (descending ? dated.ThenByDescending(t => t.Due, StringComparer.Ordinal) : dated.ThenBy(t => t.Due, StringComparer.Ordinal))
                .ThenBy(t => t.CreatedAt, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal);
        }
        public static string Countdown(string due, DateTime today)
        {
            if (due == null) return "—";
            int days = (DateTime.ParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture) - today.Date).Days;
            return days == 0 ? "今天" : days > 0 ? days + " 天后" : "已过 " + (-days) + " 天";
        }
        public static string DateText(Todo task, bool countdown, DateTime today)
        { return countdown ? Countdown(task.Due, today) : task.Due ?? "—"; }
        // Breadth-first traversal returns every ancestor/descendant once,
        // with its shortest distance. A diamond is not duplicated in list view.
        public static Dictionary<string, int> Related(State state, string id, bool upstream)
        {
            Get(state, id);
            var result = new Dictionary<string, int>();
            var queue = new Queue<string>(); queue.Enqueue(id);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                int depth = current == id ? 0 : result[current];
                IEnumerable<string> next = upstream ? Get(state, current).Prerequisites :
                    state.Tasks.Where(t => t.Prerequisites.Contains(current)).Select(t => t.Id);
                foreach (string child in next)
                    if (child != id && !result.ContainsKey(child))
                    { result.Add(child, depth + 1); queue.Enqueue(child); }
            }
            return result;
        }
        public static Todo Get(State state, string id)
        {
            Todo t = state.Tasks.FirstOrDefault(x => x.Id == id);
            if (t == null) throw new InvalidOperationException("找不到该任务。");
            return t;
        }
        public static List<Todo> Blockers(State state, Todo t)
        {
            return t.Prerequisites.Select(id => Get(state, id)).Where(x => !x.Done).ToList();
        }
        public static void Validate(State state)
        {
            if (state == null || state.Version != 1 || state.Tasks == null || state.Window == null)
                throw new InvalidOperationException("数据版本或格式不正确。");
            var ids = new HashSet<string>();
            foreach (Todo t in state.Tasks)
            {
                if (t == null || String.IsNullOrWhiteSpace(t.Id) || !ids.Add(t.Id))
                    throw new InvalidOperationException("任务 ID 重复或缺失。");
                if (String.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 120)
                    throw new InvalidOperationException("任务名称需要 1–120 个字符。");
                if (t.Description == null || t.Description.Length > 10000 || t.Prerequisites == null)
                    throw new InvalidOperationException("任务描述或前置关系格式不正确。");
                DateTime date;
                if (t.Due != null && !DateTime.TryParseExact(t.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw new InvalidOperationException("日期格式不正确。");
                DateTimeOffset stamp;
                if (!DateTimeOffset.TryParse(t.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp) ||
                    (t.CompletedAt != null && !DateTimeOffset.TryParse(t.CompletedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp)))
                    throw new InvalidOperationException("任务时间格式不正确。");
            }
            foreach (Todo t in state.Tasks)
            {
                if (t.Prerequisites.Distinct().Count() != t.Prerequisites.Count ||
                    t.Prerequisites.Any(id => !ids.Contains(id) || id == t.Id))
                    throw new InvalidOperationException("前置任务不存在、重复或指向自身。");
            }
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();
            foreach (Todo t in state.Tasks) Visit(state, t, visiting, visited);
            foreach (Todo t in state.Tasks.Where(x => x.Done))
                if (Blockers(state, t).Count > 0)
                    throw new InvalidOperationException("已完成任务仍有未完成的前置任务。");
        }
        private static void Visit(State state, Todo t, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(t.Id)) return;
            if (!visiting.Add(t.Id)) throw new InvalidOperationException("形成了循环依赖，请调整前置任务。");
            foreach (string id in t.Prerequisites) Visit(state, Get(state, id), visiting, visited);
            visiting.Remove(t.Id); visited.Add(t.Id);
        }
        public static void Complete(State state, string id)
        {
            Todo t = Get(state, id);
            if (t.Done) return;
            var blockers = Blockers(state, t);
            if (blockers.Count > 0) throw new InvalidOperationException("请先完成：" + String.Join("、", blockers.Select(x => x.Name)));
            t.CompletedAt = DateTime.UtcNow.ToString("o");
        }
        public static void Restore(State state, string id)
        {
            Todo t = Get(state, id);
            if (state.Tasks.Any(x => x.Done && x.Prerequisites.Contains(id)))
                throw new InvalidOperationException("请先恢复依赖它的已完成任务，再恢复此任务。");
            t.CompletedAt = null;
        }
        public static void Delete(State state, string id)
        {
            Todo t = Get(state, id);
            foreach (Todo other in state.Tasks) other.Prerequisites.Remove(id);
            state.Tasks.Remove(t);
        }
        public static List<Todo> CompletedAffectedByPrerequisite(State state, string id)
        {
            var affected = Related(state, id, false); affected[id] = 0;
            return state.Tasks.Where(t => t.Done && affected.ContainsKey(t.Id)).ToList();
        }
        public static void AddRelated(State state, Todo task, string anchor, bool before)
        {
            Todo target = Get(state, anchor);
            if (before)
            {
                foreach (Todo t in CompletedAffectedByPrerequisite(state, anchor)) t.CompletedAt = null;
                target.Prerequisites.Add(task.Id);
            }
            else if (!task.Prerequisites.Contains(anchor)) task.Prerequisites.Add(anchor);
            state.Tasks.Add(task);
        }
        public static void DeleteHistory(State state, string id)
        {
            Todo t = Get(state, id);
            if (!t.Done) throw new InvalidOperationException("只能在历史中删除已完成任务。");
            foreach (Todo other in state.Tasks) other.Prerequisites.Remove(id);
            state.Tasks.Remove(t);
        }
    }

    public static class WorldClock
    {
        public const string AoE = "TinyTodo/AoE";
        public const string UK = "GMT Standard Time";
        public const string China = "China Standard Time";
        private static readonly TimeZoneInfo aoe = TimeZoneInfo.CreateCustomTimeZone(AoE, TimeSpan.FromHours(-12), "AoE (UTC-12)", "AoE");
        public static TimeZoneInfo Zone(string id)
        {
            if (String.IsNullOrEmpty(id)) return TimeZoneInfo.Local;
            if (id == AoE) return aoe;
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        public static DateTimeOffset At(DateTimeOffset utc, string id)
        { return TimeZoneInfo.ConvertTime(utc, Zone(id)); }
        public static string Display(DateTimeOffset instant)
        { return instant.ToString("yyyy-MM-dd  HH:mm:ss  'UTC'zzz", CultureInfo.InvariantCulture); }
    }

    public sealed class Store
    {
        private string path;
        public State Current { get; private set; }
        public string PathName { get { return path; } }
        public Store(string path) { this.path = path; }
        internal void AdoptPath(string nextPath) { path = nextPath; }
        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 128 };
        }
        public static State Read(string file)
        {
            State result = Serializer().Deserialize<State>(File.ReadAllText(file, Encoding.UTF8));
            Rules.Validate(result); return result;
        }
        public void Load()
        {
            // Never silently replace damaged user data with an empty document.
            Current = File.Exists(path) ? Read(path) : new State();
        }
        public void RecoverBackup()
        {
            State backup = Read(path + ".bak");
            if (File.Exists(path)) File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
            Current = backup;
            Write(backup, false);
        }
        public void Change(Action<State> action)
        {
            // Mutate a copy; publish it only after durable replacement succeeds.
            State next = Serializer().Deserialize<State>(Serializer().Serialize(Current));
            action(next); Rules.Validate(next); Write(next, true); Current = next;
        }
        private void Write(State state, bool backup)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Serializer().Serialize(state));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
