using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TinyTodo
{
    public sealed class SyncException : InvalidOperationException
    {
        public bool Conflict { get; private set; }
        public SyncException(string message, bool conflict = false, Exception inner = null) : base(message, inner) { Conflict = conflict; }
    }
    public sealed class SyncData
    {
        public int Version { get; set; }
        public List<Todo> Tasks { get; set; }
        public List<FocusSession> FocusSessions { get; set; }
        public SyncData() { Version = 1; Tasks = new List<Todo>(); FocusSessions = new List<FocusSession>(); }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 }; }
        public static SyncData Capture(State state)
        {
            var result = Json().Deserialize<SyncData>(Json().Serialize(new SyncData { Tasks = state.Tasks, FocusSessions = state.FocusSessions }));
            result.Tasks = result.Tasks.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
            foreach (var task in result.Tasks) task.Prerequisites.Sort(StringComparer.Ordinal);
            result.FocusSessions = result.FocusSessions.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
            return result;
        }
        public string Encode() { return Json().Serialize(this); }
        public static SyncData Decode(string text)
        {
            try
            {
                var fields = Json().DeserializeObject(text) as Dictionary<string, object>;
                if (fields == null || !fields.ContainsKey("Version") || !fields.ContainsKey("Tasks") || !fields.ContainsKey("FocusSessions"))
                    throw new InvalidOperationException("任务数据缺少必要字段。");
                var data = Json().Deserialize<SyncData>(text);
                if (data == null || data.Version != 1) throw new InvalidOperationException("不支持的数据版本。");
                data.Validate(); return Capture(new State { Tasks = data.Tasks, FocusSessions = data.FocusSessions });
            }
            catch (ArgumentException ex) { throw new SyncException("远端任务数据无法读取，未修改本地任务。", false, ex); }
            catch (InvalidOperationException ex) { throw new SyncException("远端任务数据无效，未修改本地任务：" + ex.Message, false, ex); }
        }
        public void Validate() { Rules.Validate(new State { Tasks = Tasks, FocusSessions = FocusSessions }); }
        public void Apply(State state) { state.Tasks = Tasks; state.FocusSessions = FocusSessions; }
        public bool Same(SyncData other) { return other != null && Encode() == other.Encode(); }
        public static SyncData Merge(SyncData original, SyncData local, SyncData remote)
        {
            var merged = new SyncData {
                Tasks = MergeItems(original.Tasks, local.Tasks, remote.Tasks, t => t.Id),
                FocusSessions = MergeItems(original.FocusSessions, local.FocusSessions, remote.FocusSessions, t => t.Id)
            };
            try { merged.Validate(); }
            catch (InvalidOperationException ex) { throw new SyncException("两端修改无法合并：" + ex.Message, true, ex); }
            return Capture(new State { Tasks = merged.Tasks, FocusSessions = merged.FocusSessions });
        }
        private static List<T> MergeItems<T>(IEnumerable<T> original, IEnumerable<T> local, IEnumerable<T> remote, Func<T, string> id) where T : class, new()
        {
            var before = original.ToDictionary(id); var left = local.ToDictionary(id); var right = remote.ToDictionary(id);
            var result = new List<T>(); var json = Json();
            foreach (string key in before.Keys.Concat(left.Keys).Concat(right.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal))
            {
                T b, l, r; before.TryGetValue(key, out b); left.TryGetValue(key, out l); right.TryGetValue(key, out r);
                string bs = json.Serialize(b), ls = json.Serialize(l), rs = json.Serialize(r);
                T value;
                if (ls == rs || bs == rs) value = l;
                else if (bs == ls) value = r;
                else
                {
                    if (l == null || r == null) throw new SyncException("同一任务在一端被删除、另一端被修改。请在设置中选择保留哪一端。", true);
                    value = new T();
                    foreach (var property in typeof(T).GetProperties().Where(p => p.CanRead && p.CanWrite))
                    {
                        object bv = b == null ? null : property.GetValue(b, null), lv = property.GetValue(l, null), rv = property.GetValue(r, null), chosen;
                        string bvs = json.Serialize(bv), lvs = json.Serialize(lv), rvs = json.Serialize(rv);
                        if (lvs == rvs || (b != null && bvs == rvs)) chosen = lv;
                        else if (b != null && bvs == lvs) chosen = rv;
                        else if (typeof(T) == typeof(FocusSession) && property.Name == "Seconds") chosen = Math.Max((int)lv, (int)rv);
                        else throw new SyncException("同一任务的同一内容在两端被修改。请在设置中选择保留哪一端。", true);
                        property.SetValue(value, chosen, null);
                    }
                }
                if (value != null) result.Add(value);
            }
            return result;
        }
    }
    public sealed class SyncCache
    {
        public string Repository { get; set; }
        public SyncData Data { get; set; }
        public static SyncData Load(string file, string repository)
        {
            if (!File.Exists(file)) return new SyncData();
            try
            {
                var cache = SyncData.Json().Deserialize<SyncCache>(File.ReadAllText(file, Encoding.UTF8));
                if (cache == null || cache.Data == null || cache.Data.Version != 1 || cache.Repository == null) throw new InvalidOperationException("同步记录不完整。");
                cache.Data.Validate();
                return String.Equals(cache.Repository, repository, StringComparison.OrdinalIgnoreCase) ? cache.Data : new SyncData();
            }
            catch (ArgumentException ex) { throw new SyncException("本机同步记录损坏，请保留 sync-state.json 及备份。", false, ex); }
            catch (InvalidOperationException ex) { throw new SyncException("本机同步记录无效，请保留 sync-state.json 及备份。", false, ex); }
        }
        public static void Save(string file, string repository, SyncData data)
        {
            string temp = file + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(SyncData.Json().Serialize(new SyncCache { Repository = repository, Data = data }));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(file)) File.Replace(temp, file, file + ".bak"); else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class GitHubLogin
    {
        public static string Repository(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new SyncException("请填写 GitHub 仓库链接，例如 https://github.com/用户名/仓库名。");
            string path = uri.AbsolutePath.Trim('/'); if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            string[] parts = path.Split('/');
            if (parts.Length != 2 || parts.Any(p => !Regex.IsMatch(p, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$") || p == "." || p == "..")) throw new SyncException("仓库链接需要包含用户名和仓库名。");
            return "https://github.com/" + path;
        }
        public static string Variable(string value)
        {
            value = (value ?? "").Trim();
            if (Regex.IsMatch(value, @"^(gh[pousr]_|github_pat_)") || value.Any(c => Char.IsWhiteSpace(c) || c == '=' || c == '\0'))
                throw new SyncException("这里只填写环境变量名，不要填写 token 本身。");
            return value;
        }
        public static string Token(string variable)
        {
            variable = Variable(variable);
            if (variable.Length != 0)
            {
                string token = Environment.GetEnvironmentVariable(variable);
                if (String.IsNullOrWhiteSpace(token)) token = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User);
                if (String.IsNullOrWhiteSpace(token)) token = Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Machine);
                if (String.IsNullOrWhiteSpace(token)) throw new SyncException("未找到环境变量 " + variable + "。请设置 token，或留空以使用本机 Git 登录。");
                return token.Trim();
            }
            var info = new ProcessStartInfo("git.exe", "-c credential.interactive=false credential fill") {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; info.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            try
            {
                using (var process = Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                    // Framework may prefix stdin with a BOM; explicit fields also identify the URL.
                    byte[] query = Encoding.UTF8.GetBytes("url=https://github.com\nprotocol=https\nhost=github.com\n\n");
                    using (var input = process.StandardInput.BaseStream) input.Write(query, 0, query.Length);
                    if (!process.WaitForExit(15000)) { process.Kill(); process.WaitForExit(); throw new SyncException("读取本机 Git 登录超时。可改用 token 环境变量。"); }
                    string text = output.Result; error.Wait();
                    if (process.ExitCode == 0)
                        foreach (string line in text.Split('\n')) if (line.StartsWith("password=", StringComparison.Ordinal) && line.Length > 9) return line.Substring(9).TrimEnd('\r');
                    throw new SyncException("没有可用的本机 Git 登录。请先登录 GitHub，或填写 token 环境变量名。");
                }
            }
            catch (Win32Exception ex) { throw new SyncException("无法运行 Git。请填写 token 环境变量名，或安装 Git 并登录 GitHub。", false, ex); }
        }
    }
    public sealed class RemoteData
    {
        public string Sha { get; set; }
        public SyncData Data { get; set; }
    }
    public interface ISyncRemote
    {
        RemoteData Read();
        void Write(SyncData data, string sha);
    }
    public sealed class GitHubData : ISyncRemote
    {
        public const string FileName = "tinytodo-data.json";
        private readonly string root, token, branchOverride;
        private string branch;
        public GitHubData(string repository, string token, string branchOverride = null)
        {
            root = "https://api.github.com/repos/" + GitHubLogin.Repository(repository).Substring("https://github.com/".Length);
            this.token = token; this.branchOverride = branchOverride; ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        private Dictionary<string, object> Request(string url, string method = "GET", object body = null, bool missing = false)
        {
            var request = (HttpWebRequest)WebRequest.Create(url); request.Method = method; request.AllowAutoRedirect = false;
            request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.UserAgent = "TinyTodo"; request.Accept = "application/vnd.github+json";
            request.Headers["Authorization"] = "Bearer " + token; request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            try
            {
                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(SyncData.Json().Serialize(body)); request.ContentType = "application/json"; request.ContentLength = bytes.Length;
                    using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                }
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var text = new StringBuilder(); var buffer = new char[8192]; int length;
                    while ((length = reader.Read(buffer, 0, buffer.Length)) > 0) { text.Append(buffer, 0, length); if (text.Length > 32 * 1024 * 1024) throw new SyncException("GitHub 数据超过支持的大小。"); }
                    var data = SyncData.Json().DeserializeObject(text.ToString()) as Dictionary<string, object>;
                    if (data == null) throw new SyncException("GitHub 返回了无法识别的数据。"); return data;
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                int code = response == null ? 0 : (int)response.StatusCode; if (response != null) response.Dispose();
                if (code == 404 && missing) return null;
                if (code == 409 || code == 422) throw new SyncException("远端在同步期间发生变化，或仓库拒绝写入。请检查仓库规则后重新同步。", false, ex);
                if (code == 401 || code == 403) throw new SyncException("GitHub 拒绝授权或请求额度不足。请检查 token 的仓库 Contents 读写权限。", false, ex);
                if (code == 404) throw new SyncException("找不到仓库，或当前 GitHub 登录无权访问。", false, ex);
                throw new SyncException(code == 0 ? "无法连接 GitHub。任务已保存在本机，可稍后重试。" : "GitHub 请求失败（" + code + "），本机任务已保留。", false, ex);
            }
            catch (ArgumentException ex) { throw new SyncException("GitHub 返回的数据格式不正确。", false, ex); }
        }
        private static T Field<T>(Dictionary<string, object> data, string name)
        { object value; if (!data.TryGetValue(name, out value) || !(value is T)) throw new SyncException("GitHub 返回的数据缺少 " + name + "。"); return (T)value; }
        public RemoteData Read()
        {
            var repository = Request(root);
            if (!Field<bool>(repository, "private")) throw new SyncException("任务同步仅支持私有仓库，请先将仓库设为 Private。");
            branch = branchOverride ?? Field<string>(repository, "default_branch");
            var file = Request(root + "/contents/" + FileName + "?ref=" + Uri.EscapeDataString(branch), missing: true);
            if (file == null) return new RemoteData { Data = new SyncData() };
            string sha = Field<string>(file, "sha");
            if (Convert.ToInt64(file["size"]) > 16 * 1024 * 1024) throw new SyncException("远端任务文件超过 16 MB，未修改本地任务。");
            if (Field<string>(file, "encoding") != "base64") file = Request(root + "/git/blobs/" + sha);
            byte[] bytes;
            try { bytes = Convert.FromBase64String(Field<string>(file, "content")); }
            catch (FormatException ex) { throw new SyncException("GitHub 任务文件编码不正确。", false, ex); }
            return new RemoteData { Sha = sha, Data = SyncData.Decode(Encoding.UTF8.GetString(bytes)) };
        }
        public void Write(SyncData data, string sha)
        {
            if (branch == null) throw new InvalidOperationException("Read the remote repository before writing.");
            byte[] bytes = Encoding.UTF8.GetBytes(data.Encode()); if (bytes.Length > 16 * 1024 * 1024) throw new SyncException("同步任务数据超过 16 MB。");
            var body = new Dictionary<string, object> { { "message", "Update TinyTodo data" }, { "content", Convert.ToBase64String(bytes) }, { "branch", branch } };
            if (sha != null) body["sha"] = sha;
            Request(root + "/contents/" + FileName, "PUT", body);
        }
    }
    public enum SyncAction { Pull, Push, KeepLocal, KeepRemote }
    public sealed class SyncResult
    {
        public SyncData Data, Baseline;
    }
    public static class SyncExchange
    {
        public static SyncResult Run(ISyncRemote remote, SyncData baseline, SyncData local, SyncAction action)
        {
            var current = remote.Read();
            if (current.Sha == null && (baseline.Tasks.Count != 0 || baseline.FocusSessions.Count != 0) && action != SyncAction.KeepLocal && action != SyncAction.KeepRemote)
                throw new SyncException("远端同步文件已被删除。请在设置中选择采用本机重新上传，或采用云端空版本。", true);
            var merged = action == SyncAction.KeepLocal ? local : action == SyncAction.KeepRemote ? current.Data : SyncData.Merge(baseline, local, current.Data);
            if (action == SyncAction.Push || action == SyncAction.KeepLocal)
            {
                if (current.Sha == null || !merged.Same(current.Data)) remote.Write(merged, current.Sha);
                return new SyncResult { Data = merged, Baseline = merged };
            }
            return new SyncResult { Data = merged, Baseline = current.Data };
        }
    }
}
