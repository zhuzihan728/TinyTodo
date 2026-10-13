using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TinyTodo
{
    public enum RepeatKind { None, Daily, Weekly, Monthly }
    public enum TaskSort { Date, Priority, Created, Name, FocusTime }
    public sealed class RepeatRule
    {
        public RepeatKind Kind { get; set; }
        public int Day { get; set; }
    }
    public sealed class FocusSession
    {
        public string Id { get; set; }
        public string TaskId { get; set; }
        public string SeriesId { get; set; }
        public string StartedAt { get; set; }
        public string Day { get; set; }
        public int Seconds { get; set; }
    }
    public static class Productivity
    {
        public static string Series(Todo task) { return task.SeriesId ?? task.Id; }
        public static DateTime NextDate(RepeatRule rule, DateTime after)
        {
            after = after.Date;
            if (rule.Kind == RepeatKind.Daily) return after.AddDays(1);
            if (rule.Kind == RepeatKind.Weekly)
            {
                int days = (rule.Day - (int)after.DayOfWeek + 7) % 7;
                return after.AddDays(days == 0 ? 7 : days);
            }
            if (rule.Kind == RepeatKind.Monthly)
            {
                DateTime date = new DateTime(after.Year, after.Month, Math.Min(rule.Day, DateTime.DaysInMonth(after.Year, after.Month)));
                if (date <= after) { var month = new DateTime(after.Year, after.Month, 1).AddMonths(1); date = new DateTime(month.Year, month.Month, Math.Min(rule.Day, DateTime.DaysInMonth(month.Year, month.Month))); }
                return date;
            }
            throw new InvalidOperationException("任务没有循环规则。");
        }
        public static void AddNext(State state, Todo completed, DateTime today)
        {
            if (completed.Repeat == null || completed.Repeat.Kind == RepeatKind.None) return;
            completed.SeriesId = Series(completed);
            DateTime after = completed.Due == null ? today.Date : DateTime.ParseExact(completed.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (after < today.Date) after = today.Date;
            string date = NextDate(completed.Repeat, after).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string id = completed.SeriesId + ":" + date;
            if (state.Tasks.Any(t => t.Id == id)) return;
            state.Tasks.Add(new Todo {
                Id = id, SeriesId = completed.SeriesId, Name = completed.Name, Description = completed.Description,
                Important = completed.Important, Due = date,
                Repeat = new RepeatRule { Kind = completed.Repeat.Kind, Day = completed.Repeat.Day },
                Prerequisites = new List<string>(completed.Prerequisites)
            });
        }
        public static int CompletedCount(State state, Todo task)
        { string series = Series(task); return state.Tasks.Count(t => t.Done && Series(t) == series); }
        public static long FocusSeconds(State state, Todo task)
        { string series = Series(task); return state.FocusSessions.Where(s => s.SeriesId == series).Sum(s => (long)s.Seconds); }
        public static IDictionary<string, long> DailyFocus(State state, Todo task)
        { string series = Series(task); return state.FocusSessions.Where(s => s.SeriesId == series).GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.Sum(s => (long)s.Seconds)); }
        public static IEnumerable<Todo> History(State state)
        { return state.Tasks.Where(t => t.Done).GroupBy(Series).Select(g => g.OrderByDescending(t => t.CompletedAt, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal).First()); }
        public static IEnumerable<Todo> Sort(State state, IEnumerable<Todo> tasks)
        {
            bool descending = state.Window.SortDescending;
            IOrderedEnumerable<Todo> sorted;
            switch (state.Window.Sort)
            {
                case TaskSort.Priority: sorted = descending ? tasks.OrderBy(t => t.Important) : tasks.OrderByDescending(t => t.Important); break;
                case TaskSort.Created: sorted = descending ? tasks.OrderByDescending(t => t.CreatedAt, StringComparer.Ordinal) : tasks.OrderBy(t => t.CreatedAt, StringComparer.Ordinal); break;
                case TaskSort.Name: sorted = descending ? tasks.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase) : tasks.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase); break;
                case TaskSort.FocusTime: sorted = descending ? tasks.OrderBy(t => FocusSeconds(state, t)) : tasks.OrderByDescending(t => FocusSeconds(state, t)); break;
                default: return Rules.ByDate(tasks, state.Window.DateDescending);
            }
            return sorted.ThenBy(t => t.Due == null).ThenBy(t => t.Due, StringComparer.Ordinal).ThenBy(t => t.Id, StringComparer.Ordinal);
        }
        public static void Validate(State state)
        {
            if (state.FocusSessions == null) throw new InvalidOperationException("专注记录缺失。");
            if (!Enum.IsDefined(typeof(TaskSort), state.Window.Sort)) throw new InvalidOperationException("排序方式不正确。");
            foreach (var task in state.Tasks)
            {
                var rule = task.Repeat;
                if (task.SeriesId != null && String.IsNullOrWhiteSpace(task.SeriesId)) throw new InvalidOperationException("循环任务标识不正确。");
                if (rule != null && (!Enum.IsDefined(typeof(RepeatKind), rule.Kind) ||
                    (rule.Kind == RepeatKind.Weekly && (rule.Day < 0 || rule.Day > 6)) ||
                    (rule.Kind == RepeatKind.Monthly && (rule.Day < 1 || rule.Day > 31))))
                    throw new InvalidOperationException("循环规则不正确。");
            }
            var ids = new HashSet<string>();
            foreach (var session in state.FocusSessions)
            {
                DateTime day; DateTimeOffset start;
                if (session == null || String.IsNullOrWhiteSpace(session.Id) || !ids.Add(session.Id) ||
                    String.IsNullOrWhiteSpace(session.TaskId) || String.IsNullOrWhiteSpace(session.SeriesId) || session.Seconds < 0 || session.Seconds > 86400 ||
                    !DateTime.TryParseExact(session.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ||
                    !DateTimeOffset.TryParse(session.StartedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
                    throw new InvalidOperationException("专注记录格式不正确。");
            }
        }
    }
    public sealed class FocusTimer
    {
        private readonly Store store;
        private readonly Func<DateTimeOffset> now;
        private DateTimeOffset started;
        private DateTimeOffset? pausedAt;
        private string runId, series;
        private int recorded, duration;
        public string TaskId { get; private set; }
        public bool Running { get { return TaskId != null && !Paused; } }
        public bool Paused { get { return pausedAt.HasValue; } }
        public int DurationSeconds { get { return duration; } }
        public int RemainingSeconds { get { return TaskId != null ? Math.Max(0, duration - Elapsed) : 0; } }
        private int Elapsed { get { return Math.Min(duration, Math.Max(0, (int)((pausedAt ?? now()) - started).TotalSeconds)); } }
        public FocusTimer(Store store, Func<DateTimeOffset> now) { this.store = store; this.now = now; }
        public void Start(string taskId, int minutes)
        {
            if (minutes < 1 || minutes > 1440) throw new InvalidOperationException("专注时间需要在 1–1440 分钟之间。");
            if (TaskId != null) throw new InvalidOperationException("请先暂停或结束当前专注。");
            var task = Rules.Get(store.Current, taskId);
            if (task.Done) throw new InvalidOperationException("已完成任务不能开始新的专注，请选择当前待办。");
            pausedAt = null; started = now(); duration = minutes * 60; recorded = 0;
            series = Productivity.Series(task); runId = Guid.NewGuid().ToString("N"); TaskId = taskId;
        }
        public void Tick()
        {
            if (TaskId == null) return;
            var task = store.Current.Tasks.FirstOrDefault(t => t.Id == TaskId);
            if (Paused) { if (task == null || task.Done) Stop(); return; }
            int elapsed = Elapsed;
            bool ended = elapsed == duration || task == null || task.Done;
            if (elapsed - recorded >= 15 || ended) Record(elapsed);
            if (ended) TaskId = null;
        }
        public void Hold() { if (Running) pausedAt = now(); }
        public void Pause() { Hold(); if (TaskId != null) Record(Elapsed); }
        public void Resume()
        {
            if (TaskId == null || !Paused) return;
            Record(Elapsed); started += now() - pausedAt.Value; pausedAt = null;
        }
        public void Stop()
        { if (TaskId == null) return; Pause(); TaskId = null; pausedAt = null; }
        private void Record(int elapsed)
        {
            if (elapsed <= recorded) return;
            DateTimeOffset cursor = started.AddSeconds(recorded);
            int remaining = elapsed - recorded;
            var additions = new List<FocusSession>();
            while (remaining > 0)
            {
                DateTimeOffset local = cursor.ToLocalTime();
                DateTime midnight = local.Date.AddDays(1);
                DateTimeOffset boundary = new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
                int seconds = Math.Min(remaining, Math.Max(1, (int)Math.Ceiling((boundary - cursor).TotalSeconds)));
                string day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                additions.Add(new FocusSession { Id = runId + ":" + day, TaskId = TaskId, SeriesId = series,
                    StartedAt = cursor.ToString("o"), Day = day, Seconds = seconds });
                remaining -= seconds; cursor = cursor.AddSeconds(seconds);
            }
            store.Change(state => {
                foreach (var addition in additions)
                {
                    var existing = state.FocusSessions.FirstOrDefault(s => s.Id == addition.Id);
                    if (existing == null) state.FocusSessions.Add(addition); else existing.Seconds += addition.Seconds;
                }
            });
            recorded = elapsed;
        }
    }

}
