using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopTodo
{
    public sealed class TodoRepository
    {
        private readonly string root;
        private readonly string dataPath;
        private readonly string backupPath;
        private readonly JavaScriptSerializer json;
        private DateTime retentionToday;
        public TodoState State { get; private set; }
        public string StartupNotice { get; private set; }

        public TodoRepository(string rootDirectory)
        {
            if (String.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("未指定数据目录。");
            root = Path.GetFullPath(rootDirectory);
            dataPath = Path.Combine(root, "data.json");
            backupPath = Path.Combine(root, "data.backup.json");
            json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 100 };
            retentionToday = DateTime.Today;
        }

        public void Load(DateTimeOffset now)
        {
            Directory.CreateDirectory(root);
            retentionToday = now.Date;
            StartupNotice = null;
            if (!File.Exists(dataPath) && !File.Exists(backupPath))
            {
                State = NewState();
                Save();
                return;
            }

            Exception primaryError = null;
            if (File.Exists(dataPath))
            {
                try { State = ReadState(dataPath); }
                catch (UnsupportedVersionException) { throw; }
                catch (Exception ex) { primaryError = ex; }
            }
            else primaryError = new InvalidDataException("data.json 不存在。");

            if (primaryError != null)
            {
                TodoState recovered;
                try
                {
                    if (!File.Exists(backupPath)) throw new FileNotFoundException("没有可恢复的备份。");
                    recovered = ReadState(backupPath);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("待办数据无法读取，已保留原文件，未覆盖为空列表。请检查 data.json 或恢复 data.backup.json。\n" + primaryError.Message + "\n" + ex.Message, ex);
                }
                if (File.Exists(dataPath))
                    File.Copy(dataPath, Path.Combine(root, "data.corrupt.json"), true);
                State = recovered;
                RemoveExpiredTerminalItems(State, retentionToday.AddDays(-29));
                AtomicWrite(dataPath, Serialize(State), null);
                StartupNotice = "待办数据已从最近有效备份恢复。原异常文件保存在 data.corrupt.json（如原文件存在），请检查近期操作。";
            }
            ArchiveAndPrune(now);
        }

        public void Save()
        {
            if (State == null) throw new InvalidOperationException("请先读取待办数据。");
            ValidateState(State);
            string next = Serialize(State);
            // Finish the backup before the live commit. A reported failure then always means
            // the previous live file is intact, allowing the caller to roll its UI state back.
            if (File.Exists(dataPath))
            {
                TodoState previous = ReadState(dataPath);
                RemoveExpiredTerminalItems(previous, retentionToday.AddDays(-29));
                AtomicWrite(backupPath, Serialize(previous), null);
            }
            AtomicWrite(dataPath, next, null);
        }

        public bool ArchiveAndPrune(DateTimeOffset now)
        {
            if (State == null) throw new InvalidOperationException("请先读取待办数据。");
            retentionToday = now.Date;
            DateTime cutoff = retentionToday.AddDays(-29);
            string archiveDir = Path.Combine(root, "archive");
            Directory.CreateDirectory(archiveDir);
            bool changed = false;

            // Only remove owned, date-named archive files. Unknown user files are untouched.
            foreach (string file in Directory.GetFiles(archiveDir))
            {
                string name = Path.GetFileName(file);
                string datePart = null;
                if (name.Length == 15 && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) datePart = name.Substring(0, 10);
                else if (name.Length == 19 && name.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) datePart = name.Substring(0, 10);
                DateTime date;
                if (datePart != null && DateTime.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) && date < cutoff)
                {
                    File.Delete(file);
                    changed = true;
                }
            }

            List<TodoItem> terminal = State.Items.Where(x => EventTime(x).HasValue && EventTime(x).Value.Date < retentionToday).ToList();
            foreach (var group in terminal.Where(x => EventTime(x).Value.Date >= cutoff).GroupBy(x => EventTime(x).Value.Date))
            {
                string date = group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = Path.Combine(archiveDir, date + ".json");
                ArchiveDocument document;
                if (File.Exists(path))
                {
                    try
                    {
                        document = json.Deserialize<ArchiveDocument>(File.ReadAllText(path, Encoding.UTF8));
                        if (document == null || document.Version != 1 || document.Date != date || document.Items == null)
                            throw new InvalidDataException("归档结构不正确。");
                        if (document.Items.Any(x => x == null || String.IsNullOrWhiteSpace(x.Id)))
                            throw new InvalidDataException("归档事项缺少标识。");
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("归档文件无法读取，当前事项已保留，请检查：" + path, ex);
                    }
                }
                else document = new ArchiveDocument { Date = date };

                // Archive first, then remove from the live document. Repeated attempts upsert by ID.
                Dictionary<string, ArchivedTodo> byId = new Dictionary<string, ArchivedTodo>(StringComparer.Ordinal);
                foreach (ArchivedTodo archived in document.Items) byId[archived.Id] = archived;
                foreach (TodoItem item in group)
                {
                    TodoCategory category = State.Categories.FirstOrDefault(x => x.Id == item.CategoryId);
                    byId[item.Id] = new ArchivedTodo
                    {
                        Id = item.Id, CategoryId = item.CategoryId, CategoryName = category == null ? "默认" : category.Name,
                        Title = item.Title, CreatedAt = item.CreatedAt, DueAt = item.DueAt,
                        CompletedAt = item.CompletedAt, DeletedAt = item.DeletedAt
                    };
                }
                document.Items = byId.Values.OrderBy(x => x.CreatedAt, StringComparer.Ordinal).ToList();
                AtomicWrite(path, Serialize(document), null);
            }

            if (terminal.Count > 0)
            {
                HashSet<string> ids = new HashSet<string>(terminal.Select(x => x.Id), StringComparer.Ordinal);
                List<TodoItem> previous = State.Items;
                State.Items = State.Items.Where(x => !ids.Contains(x.Id)).ToList();
                try { Save(); }
                catch { State.Items = previous; throw; }
                changed = true;
            }
            if (terminal.Count == 0) PruneBackup(cutoff);
            return changed;
        }

        private TodoState ReadState(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, object> envelope = json.DeserializeObject(text) as Dictionary<string, object>;
            if (envelope == null || !envelope.ContainsKey("Version")) throw new InvalidDataException("数据缺少 Version 字段。");
            int version;
            if (!Int32.TryParse(Convert.ToString(envelope["Version"], CultureInfo.InvariantCulture), out version))
                throw new InvalidDataException("数据版本格式不正确。");
            if (version > 1) throw new UnsupportedVersionException("此数据由更新版本的桌面待办创建，请使用新版程序打开；文件未修改。");
            if (version != 1 || !envelope.ContainsKey("Categories") || !envelope.ContainsKey("Items") || !envelope.ContainsKey("Settings"))
                throw new InvalidDataException("待办数据结构不完整。");
            // A malformed appearance preference must not roll back otherwise valid tasks.
            // Normalize this one field before typed conversion; absent fields retain constructor defaults.
            Dictionary<string, object> settings = envelope["Settings"] as Dictionary<string, object>;
            if (settings != null && settings.ContainsKey("UiScale"))
            {
                double scale;
                if (!Double.TryParse(Convert.ToString(settings["UiScale"], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                    scale = 1.0;
                settings["UiScale"] = AppSettings.NormalizeUiScale(scale);
            }
            TodoState value = json.ConvertToType<TodoState>(envelope);
            ValidateState(value);
            return value;
        }

        private static void ValidateState(TodoState value)
        {
            if (value == null || value.Version != 1 || value.Categories == null || value.Items == null || value.Settings == null)
                throw new InvalidDataException("待办数据结构不正确。");
            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            foreach (TodoCategory category in value.Categories)
            {
                if (category == null || String.IsNullOrWhiteSpace(category.Id) || String.IsNullOrWhiteSpace(category.Name) || !categories.Add(category.Id))
                    throw new InvalidDataException("分类名称或标识无效，或存在重复标识。");
            }
            if (!categories.Contains("inbox")) throw new InvalidDataException("缺少默认接收分类（inbox）。");
            HashSet<string> items = new HashSet<string>(StringComparer.Ordinal);
            foreach (TodoItem item in value.Items)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Id) || !items.Add(item.Id) || String.IsNullOrWhiteSpace(item.Title) || !categories.Contains(item.CategoryId))
                    throw new InvalidDataException("事项标题、分类或标识无效，或存在重复标识。");
                if (!Deadline.Parse(item.CreatedAt).HasValue) throw new InvalidDataException("事项创建时间无效：" + item.Title);
                ValidateOptionalDate(item.DueAt, "截止时间", item.Title);
                ValidateOptionalDate(item.CompletedAt, "完成时间", item.Title);
                ValidateOptionalDate(item.DeletedAt, "删除时间", item.Title);
            }
            AppSettings s = value.Settings;
            s.UiScale = AppSettings.NormalizeUiScale(s.UiScale);
            if (Double.IsNaN(s.BackgroundOpacity) || Double.IsInfinity(s.BackgroundOpacity) || s.BackgroundOpacity < 0 || s.BackgroundOpacity > 1)
                throw new InvalidDataException("底板不透明度应在 0 到 1 之间。");
            if (s.Theme != "auto" && s.Theme != "light" && s.Theme != "dark") throw new InvalidDataException("主题应为 auto、light 或 dark。");
            if (!categories.Contains(s.ActiveCategoryId)) s.ActiveCategoryId = value.Categories[0].Id;
        }

        private static void ValidateOptionalDate(string text, string field, string title)
        {
            if (!String.IsNullOrEmpty(text) && !Deadline.Parse(text).HasValue)
                throw new InvalidDataException(field + "无效：" + title);
        }

        private void PruneBackup(DateTime cutoff)
        {
            if (!File.Exists(backupPath)) return;
            TodoState backup;
            try { backup = ReadState(backupPath); }
            catch { return; } // A broken backup is never substituted for valid current data.
            if (RemoveExpiredTerminalItems(backup, cutoff)) AtomicWrite(backupPath, Serialize(backup), null);
        }

        private static bool RemoveExpiredTerminalItems(TodoState value, DateTime cutoff)
        {
            return value.Items.RemoveAll(x => EventTime(x).HasValue && EventTime(x).Value.Date < cutoff) > 0;
        }

        private static DateTimeOffset? EventTime(TodoItem item)
        {
            return !String.IsNullOrEmpty(item.DeletedAt) ? Deadline.Parse(item.DeletedAt) : Deadline.Parse(item.CompletedAt);
        }

        private static TodoState NewState()
        {
            TodoState state = new TodoState();
            state.Categories.Add(new TodoCategory { Id = "inbox", Name = "默认" });
            return state;
        }

        private string Serialize(object value)
        {
            // Pretty printing is intentionally local and string-aware, so the file remains easy to edit.
            string compact = json.Serialize(value);
            StringBuilder result = new StringBuilder();
            bool inString = false, escaped = false;
            int depth = 0;
            foreach (char c in compact)
            {
                if (inString)
                {
                    result.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; result.Append(c); }
                else if (c == '{' || c == '[') { result.Append(c).AppendLine(); depth++; result.Append(' ', depth * 2); }
                else if (c == '}' || c == ']') { depth--; result.AppendLine().Append(' ', depth * 2).Append(c); }
                else if (c == ',') result.Append(c).AppendLine().Append(' ', depth * 2);
                else if (c == ':') result.Append(": ");
                else result.Append(c);
            }
            return result.AppendLine().ToString();
        }

        private static void AtomicWrite(string path, string text, string backup)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, backup, true);
            else File.Move(temporary, path);
        }

        private sealed class UnsupportedVersionException : NotSupportedException
        {
            public UnsupportedVersionException(string message) : base(message) { }
        }
    }
}
