using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TinyTodo
{
    public sealed class LocationRecord
    {
        public int Version { get; set; }
        public string CurrentDirectory { get; set; }
        public List<string> KnownDirectories { get; set; }
        public LocationRecord() { Version = 1; KnownDirectories = new List<string>(); }
    }
    public sealed class DataLocations
    {
        public const string MarkerName = ".tinytodo-data";
        public const string MarkerText = "TinyTodo owned task files v1";
        public string MetadataDirectory { get; private set; }
        public LocationRecord Record { get; private set; }
        public string TaskPath { get { return Path.Combine(Record.CurrentDirectory, "tasks.json"); } }
        private string RegistryPath { get { return Path.Combine(MetadataDirectory, "data-locations.json"); } }
        public DataLocations(string metadataDirectory) { MetadataDirectory = Path.GetFullPath(metadataDirectory); }
        public static DataLocations Default()
        {
            return new DataLocations(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyTodo"));
        }
        public void Load(bool forCleanup = false)
        {
            if (File.Exists(RegistryPath))
            {
                Record = new JavaScriptSerializer().Deserialize<LocationRecord>(File.ReadAllText(RegistryPath, Encoding.UTF8));
                if (Record == null || Record.Version != 1 || String.IsNullOrWhiteSpace(Record.CurrentDirectory) || Record.KnownDirectories == null)
                    throw new InvalidOperationException("数据位置配置损坏，请保留 data-locations.json 和 .bak 文件以便恢复。");
                Record.CurrentDirectory = Path.GetFullPath(Record.CurrentDirectory);
                if (!Record.KnownDirectories.Any(p => Same(p, Record.CurrentDirectory)))
                    throw new InvalidOperationException("数据位置记录不完整，请勿手动修改配置。");
                // A missing external drive must never silently become an empty todo list.
                if (!forCleanup && (!Directory.Exists(Record.CurrentDirectory) || !Owned(Record.CurrentDirectory)))
                    throw new InvalidOperationException("无法访问任务数据目录：\n" + Record.CurrentDirectory + "\n请连接对应磁盘或恢复该目录后再启动。");
            }
            else
            {
                Directory.CreateDirectory(MetadataDirectory);
                Record = new LocationRecord { CurrentDirectory = MetadataDirectory };
                Record.KnownDirectories.Add(MetadataDirectory);
                Mark(MetadataDirectory); SaveRecord(Record);
            }
        }
        public static bool Same(string a, string b)
        { return String.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        private static void Mark(string directory)
        {
            string file = Path.Combine(directory, MarkerName);
            if (File.Exists(file) && File.ReadAllText(file) != MarkerText) throw new InvalidOperationException("数据目录标识冲突。");
            File.WriteAllText(file, MarkerText, Encoding.UTF8);
        }
        public static bool Owned(string directory)
        {
            string file = Path.Combine(directory, MarkerName);
            return File.Exists(file) && File.ReadAllText(file, Encoding.UTF8) == MarkerText;
        }
        public static IEnumerable<string> TaskFiles(string directory)
        {
            if (!Directory.Exists(directory)) return new string[0];
            return Directory.GetFiles(directory).Where(p => Regex.IsMatch(Path.GetFileName(p),
                @"^tasks\.json(?:\.bak|\.damaged-[a-fA-F0-9]{32}|\.[a-fA-F0-9]{32}\.tmp)?$", RegexOptions.IgnoreCase));
        }
        private void SaveRecord(LocationRecord next)
        {
            Directory.CreateDirectory(MetadataDirectory);
            string temp = RegistryPath + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(next));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(RegistryPath)) File.Replace(temp, RegistryPath, RegistryPath + ".bak");
                else File.Move(temp, RegistryPath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        // Returns a warning if old copies could not be cleaned. Both locations stay registered.
        public string MoveTo(Store store, string selectedParent)
        {
            string target = Path.GetFullPath(Path.Combine(selectedParent, "TinyTodo-Data"));
            string source = Record.CurrentDirectory;
            if (Same(source, target)) return "";
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !Owned(target))
                throw new InvalidOperationException("目标 TinyTodo-Data 文件夹不为空，且不是本应用的数据目录。请选择其他位置。");
            if (TaskFiles(target).Any())
                throw new InvalidOperationException("目标目录已有任务文件。为避免覆盖，请选择一个新的空目录。");
            store.Change(s => { }); // Ensure even an empty list exists before migration.
            bool alreadyOwned = Owned(target);
            Directory.CreateDirectory(target); Mark(target);
            var copied = new List<string>();
            try
            {
                foreach (string file in TaskFiles(source))
                {
                    string destination = Path.Combine(target, Path.GetFileName(file));
                    copied.Add(destination); File.Copy(file, destination, false);
                }
                Store.Read(Path.Combine(target, "tasks.json"));
                var next = new LocationRecord { CurrentDirectory = target, KnownDirectories = new List<string>(Record.KnownDirectories) };
                if (!next.KnownDirectories.Any(p => Same(p, target))) next.KnownDirectories.Add(target);
                SaveRecord(next); Record = next; store.AdoptPath(Path.Combine(target, "tasks.json"));
            }
            catch
            {
                foreach (string file in copied) { try { File.Delete(file); } catch { } }
                if (!alreadyOwned)
                {
                    try
                    {
                        if (!TaskFiles(target).Any())
                        {
                            File.Delete(Path.Combine(target, MarkerName));
                            if (!Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
                        }
                    }
                    catch { }
                }
                throw;
            }
            try { foreach (string file in TaskFiles(source)) File.Delete(file); }
            catch (Exception ex) { return "位置已切换。旧位置的部分副本无法删除，已记录供卸载时重试：\n" + ex.Message; }
            return "";
        }
        public IEnumerable<string> KnownLocations()
        { return Record.KnownDirectories.Concat(new string[] { MetadataDirectory }).Distinct(StringComparer.OrdinalIgnoreCase); }
        public void RemoveOwnedData()
        {
            // Do not recursively remove a user-selected directory. Delete app-owned names only.
            foreach (string directory in KnownLocations())
            {
                if (!Directory.Exists(Path.GetPathRoot(Path.GetFullPath(directory))))
                    throw new InvalidOperationException("数据盘未连接，无法彻底清理：" + directory + "。请连接后重试。");
                if (!Directory.Exists(directory)) continue;
                bool legacy = Same(directory, MetadataDirectory);
                if (!legacy && !Owned(directory))
                {
                    if (TaskFiles(directory).Any()) throw new InvalidOperationException("缺少目录标识，未删除：" + directory);
                    continue; // An earlier cleanup may already have removed its marker.
                }
                foreach (string file in TaskFiles(directory)) File.Delete(file);
            }
            // Only remove the locator once every accessible registered location succeeded.
            foreach (string directory in KnownLocations())
            {
                if (!Directory.Exists(directory)) continue;
                string marker = Path.Combine(directory, MarkerName);
                if (Owned(directory)) File.Delete(marker);
                if (!Same(directory, MetadataDirectory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            foreach (string suffix in new string[] { "", ".bak", ".tmp" })
                if (File.Exists(RegistryPath + suffix)) File.Delete(RegistryPath + suffix);
            if (Directory.Exists(MetadataDirectory) && !Directory.EnumerateFileSystemEntries(MetadataDirectory).Any()) Directory.Delete(MetadataDirectory);
        }
    }
}
