using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DesktopTodo.Tests
{
    public static class StorageTests
    {
        private static int passed;
        private static int failed;
        private static string root;
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(8));
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static int Main() { return Run(); }

        public static int Run()
        {
            passed = failed = 0;
            root = Path.GetFullPath(Path.Combine("work", "storage-tests-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(root);
            Test("initial state is empty and has safe defaults", InitialState);
            Test("JSON roundtrip preserves Chinese and special characters", Roundtrip);
            Test("30-day cutoff retains unfinished items and boundary archive", Retention);
            Test("deleted date takes precedence over completed date", DeletedDate);
            Test("archive can be replayed after interruption without duplication", ArchiveIdempotency);
            Test("broken archive does not remove current items", BrokenArchive);
            Test("corrupt current file restores valid backup and quarantines original", RestoreBackup);
            Test("corrupt file without backup is not overwritten", CorruptBlocked);
            Test("future schema version does not fall back to old backup", FutureVersion);
            Test("retention also removes expired records from backup", BackupRetention);
            Test("unknown user files in archive folder are not deleted", UnknownFiles);
            Test("failed backup commit leaves live file unchanged", FailedSave);
            Test("failed live archive commit restores in-memory items", FailedArchiveCommit);
            Test("deadline stages and formatting use current wall clock", Deadlines);
            string summary = "Passed: " + passed + "; Failed: " + failed + "; Evidence: " + root;
            Console.WriteLine(summary);
            File.WriteAllText(Path.Combine(root, "result.txt"), summary, new UTF8Encoding(false));
            return failed == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }

        private static string Folder(string name)
        {
            string path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static TodoItem Item(string id, DateTimeOffset? completed, DateTimeOffset? deleted)
        {
            return new TodoItem
            {
                Id = id, CategoryId = "inbox", Title = "事项 " + id,
                CreatedAt = Now.AddDays(-90).ToString("o"),
                CompletedAt = completed.HasValue ? completed.Value.ToString("o") : null,
                DeletedAt = deleted.HasValue ? deleted.Value.ToString("o") : null
            };
        }

        private static TodoRepository Open(string name)
        {
            TodoRepository repository = new TodoRepository(Folder(name));
            repository.Load(Now);
            return repository;
        }

        private static void Assert(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }

        private static void InitialState()
        {
            TodoRepository repo = Open("initial");
            Assert(repo.State.Items.Count == 0, "Must not ship demo tasks.");
            Assert(repo.State.Categories.Count == 1 && repo.State.Categories.Single().Id == "inbox" && repo.State.Categories.Single().Name == "默认", "Initial state must contain only the default category.");
            Assert(repo.State.Settings.ActiveCategoryId == "inbox", "Default category must be selected.");
            Assert(repo.State.Settings.BackgroundOpacity == .35 && !repo.State.Settings.Topmost && repo.State.Settings.Animations, "Defaults mismatch.");
            Assert(File.Exists(Path.Combine(Folder("initial"), "data.json")), "New state not persisted.");
        }

        private static void Roundtrip()
        {
            TodoRepository repo = Open("roundtrip");
            TodoItem item = Item("roundtrip", null, null);
            item.Title = "中文：学习 \\\"引号\\\"\n下一行 { [ ] }";
            item.DueAt = Now.AddHours(7).ToString("o");
            repo.State.Items.Add(item);
            repo.Save();
            TodoRepository reopened = Open("roundtrip");
            Assert(reopened.State.Items.Single().Title == item.Title, "Title did not roundtrip.");
            Assert(reopened.State.Items.Single().DueAt == item.DueAt, "Deadline did not roundtrip.");
        }

        private static void Retention()
        {
            TodoRepository repo = Open("retention");
            repo.State.Items.Add(Item("active-old", null, null));
            repo.State.Items.Add(Item("boundary", Now.AddDays(-29), null));
            repo.State.Items.Add(Item("expired", Now.AddDays(-30), null));
            repo.State.Items.Add(Item("today", Now, null));
            repo.Save();
            repo.ArchiveAndPrune(Now);
            Assert(repo.State.Items.Count == 2 && repo.State.Items.Any(x => x.Id == "active-old") && repo.State.Items.Any(x => x.Id == "today"), "Incorrect current items after retention.");
            Assert(File.Exists(Path.Combine(Folder("retention"), "archive", "2026-10-03.json")), "Boundary day missing.");
            Assert(!File.Exists(Path.Combine(Folder("retention"), "archive", "2026-10-02.json")), "Expired day retained.");
            repo.ArchiveAndPrune(Now.AddDays(1));
            Assert(!File.Exists(Path.Combine(Folder("retention"), "archive", "2026-10-03.json")), "Boundary did not roll forward.");
            Assert(repo.State.Items.Single().Id == "active-old", "Unfinished item was lost.");
        }

        private static void DeletedDate()
        {
            TodoRepository repo = Open("deleted");
            repo.State.Items.Add(Item("deleted", Now.AddDays(-40), Now.AddDays(-1)));
            repo.Save(); repo.ArchiveAndPrune(Now);
            string path = Path.Combine(Folder("deleted"), "archive", "2026-10-31.json");
            ArchiveDocument archive = Json.Deserialize<ArchiveDocument>(File.ReadAllText(path));
            Assert(archive.Items.Single().CategoryName == "默认", "Category snapshot lost.");
            Assert(archive.Items.Single().DeletedAt != null, "Deletion timestamp lost.");
        }

        private static void ArchiveIdempotency()
        {
            TodoRepository repo = Open("idempotent");
            repo.State.Items.Add(Item("once", Now.AddDays(-1), null)); repo.Save();
            string current = Path.Combine(Folder("idempotent"), "data.json");
            string before = File.ReadAllText(current);
            repo.ArchiveAndPrune(Now);
            File.WriteAllText(current, before, new UTF8Encoding(false)); // Simulate stop after archive commit, before live commit.
            TodoRepository recovered = Open("idempotent");
            ArchiveDocument archive = Json.Deserialize<ArchiveDocument>(File.ReadAllText(Path.Combine(Folder("idempotent"), "archive", "2026-10-31.json")));
            Assert(archive.Items.Count == 1 && recovered.State.Items.Count == 0, "Replay duplicated or lost a task.");
        }

        private static void BrokenArchive()
        {
            TodoRepository repo = Open("broken-archive");
            repo.State.Items.Add(Item("preserve", Now.AddDays(-1), null)); repo.Save();
            string dir = Path.Combine(Folder("broken-archive"), "archive"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "2026-10-31.json"), "broken");
            bool thrown = false;
            try { repo.ArchiveAndPrune(Now); } catch (InvalidDataException) { thrown = true; }
            Assert(thrown && repo.State.Items.Count == 1, "Broken archive must not silently erase a current task.");
        }

        private static void RestoreBackup()
        {
            TodoRepository repo = Open("restore");
            repo.State.Items.Add(Item("backed-up", null, null)); repo.Save(); repo.Save();
            string path = Path.Combine(Folder("restore"), "data.json");
            File.WriteAllText(path, "{broken", new UTF8Encoding(false));
            TodoRepository restored = Open("restore");
            Assert(restored.State.Items.Single().Id == "backed-up" && restored.StartupNotice != null, "Backup recovery failed.");
            Assert(File.ReadAllText(Path.Combine(Folder("restore"), "data.corrupt.json")) == "{broken", "Original damaged file was not preserved.");
        }

        private static void CorruptBlocked()
        {
            string path = Path.Combine(Folder("blocked"), "data.json");
            File.WriteAllText(path, "not JSON");
            bool thrown = false;
            try { Open("blocked"); } catch (InvalidDataException) { thrown = true; }
            Assert(thrown && File.ReadAllText(path) == "not JSON", "Damaged data was overwritten.");
        }

        private static void FutureVersion()
        {
            TodoRepository repo = Open("future"); repo.Save();
            string path = Path.Combine(Folder("future"), "data.json");
            string text = File.ReadAllText(path).Replace("\"Version\": 1", "\"Version\": 2");
            File.WriteAllText(path, text);
            bool thrown = false;
            try { Open("future"); } catch (NotSupportedException) { thrown = true; }
            Assert(thrown && File.ReadAllText(path) == text, "Future version was silently downgraded.");
        }

        private static void BackupRetention()
        {
            TodoRepository repo = Open("backup-retention");
            repo.State.Items.Add(Item("old", Now.AddDays(-29), null)); repo.State.Items.Add(Item("active", null, null)); repo.Save(); repo.Save();
            repo.ArchiveAndPrune(Now.AddDays(1));
            TodoState backup = Json.Deserialize<TodoState>(File.ReadAllText(Path.Combine(Folder("backup-retention"), "data.backup.json")));
            Assert(!backup.Items.Any(x => x.Id == "old") && backup.Items.Any(x => x.Id == "active"), "Backup retained expired history or lost active task.");
        }

        private static void UnknownFiles()
        {
            TodoRepository repo = Open("unknown-files");
            string dir = Path.Combine(Folder("unknown-files"), "archive"); Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "my-note.json"); File.WriteAllText(path, "user data");
            repo.ArchiveAndPrune(Now);
            Assert(File.ReadAllText(path) == "user data", "Unknown user file was changed.");
        }

        private static void Deadlines()
        {
            TodoItem item = Item("clock", null, null);
            Assert(Deadline.Stage(item, Now) == 0 && Deadline.Format(item, Now) == String.Empty, "No deadline should be quiet.");
            item.DueAt = Now.AddHours(24).AddSeconds(1).ToString("o"); Assert(Deadline.Stage(item, Now) == 0, "Early reminder.");
            item.DueAt = Now.AddHours(24).ToString("o"); Assert(Deadline.Stage(item, Now) == 1, "24h boundary.");
            item.DueAt = Now.AddHours(1).ToString("o"); Assert(Deadline.Stage(item, Now) == 2, "1h boundary.");
            item.DueAt = Now.ToString("o"); Assert(Deadline.Stage(item, Now) == 3 && Deadline.Format(item, Now) == "刚刚到期", "Due instant boundary.");
            item.DueAt = Now.AddMinutes(30).ToString("o"); Assert(Deadline.Stage(item, Now.AddHours(2)) == 3, "Wake-up must recalculate actual time.");
            item.CompletedAt = Now.ToString("o"); Assert(Deadline.Stage(item, Now.AddHours(2)) == 0, "Completed task should not nag.");
        }

        private static void FailedSave()
        {
            TodoRepository repo = Open("save-failure");
            repo.State.Items.Add(Item("original", null, null)); repo.Save();
            string main = Path.Combine(Folder("save-failure"), "data.json");
            string before = File.ReadAllText(main);
            repo.State.Items[0].Title = "not yet committed";
            bool thrown = false;
            using (FileStream locked = new FileStream(Path.Combine(Folder("save-failure"), "data.backup.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { repo.Save(); } catch (IOException) { thrown = true; }
            }
            Assert(thrown && File.ReadAllText(main) == before, "Save signalled failure after changing the live file.");
        }

        private static void FailedArchiveCommit()
        {
            TodoRepository repo = Open("archive-commit-failure");
            repo.State.Items.Add(Item("preserved", Now.AddDays(-1), null)); repo.Save();
            string main = Path.Combine(Folder("archive-commit-failure"), "data.json");
            string before = File.ReadAllText(main);
            bool thrown = false;
            using (FileStream locked = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { repo.ArchiveAndPrune(Now); } catch (IOException) { thrown = true; }
            }
            Assert(thrown && repo.State.Items.Count == 1 && File.ReadAllText(main) == before, "Failed archive did not restore live/memory state.");
            repo.ArchiveAndPrune(Now);
            ArchiveDocument archive = Json.Deserialize<ArchiveDocument>(File.ReadAllText(Path.Combine(Folder("archive-commit-failure"), "archive", "2026-10-31.json")));
            Assert(repo.State.Items.Count == 0 && archive.Items.Count == 1, "Retry after write fault did not converge.");
        }
    }
}
