using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DesktopTodo.Tests
{
    public static class TodoOrderTests
    {
        private static int passed;
        private static int failed;
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.FromHours(8));

        public static int Main()
        {
            Test("move before and after keeps requested pending order", MoveBothDirections);
            Test("adjacent and self drops are no-ops without replacing the list", NoOp);
            Test("cross-category, completed, deleted and missing items are rejected", RejectInvalidTargets);
            Test("unrelated slots and every item reference stay intact", PreserveSlots);
            Test("saved order survives repository Save and Load", Persistence);
            Console.WriteLine("TodoOrder passed: " + passed + "; failed: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }

        private static void Assert(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason);
        }

        private static TodoItem Item(string id, string category)
        {
            return new TodoItem { Id = id, CategoryId = category, Title = "事项 " + id, CreatedAt = Now.ToString("o", CultureInfo.InvariantCulture) };
        }

        private static TodoState State()
        {
            TodoState state = new TodoState();
            state.Categories.Add(new TodoCategory { Id = "inbox", Name = "未分类" });
            state.Categories.Add(new TodoCategory { Id = "work", Name = "工作" });
            TodoItem done = Item("done", "inbox");
            done.CompletedAt = Now.ToString("o", CultureInfo.InvariantCulture);
            TodoItem deleted = Item("deleted", "inbox");
            deleted.DeletedAt = Now.ToString("o", CultureInfo.InvariantCulture);
            state.Items.AddRange(new TodoItem[]
            {
                Item("a", "inbox"), Item("x", "work"), done, Item("b", "inbox"),
                deleted, Item("c", "inbox"), Item("y", "work"), Item("d", "inbox")
            });
            return state;
        }

        private static string Order(TodoState state)
        {
            return String.Join(",", state.Items.Where(x => x.CategoryId == "inbox" && String.IsNullOrEmpty(x.CompletedAt) && String.IsNullOrEmpty(x.DeletedAt)).Select(x => x.Id));
        }

        private static void MoveBothDirections()
        {
            TodoState state = State();
            Assert(TodoOrder.MovePending(state, "inbox", "d", "b", false), "upward move must report a change");
            Assert(Order(state) == "a,d,b,c", "move before target");
            Assert(TodoOrder.MovePending(state, "inbox", "a", "b", true), "downward move must report a change");
            Assert(Order(state) == "d,b,a,c", "move after target");
            Assert(TodoOrder.MovePending(state, "inbox", "d", "c", true), "move to the end");
            Assert(Order(state) == "b,a,c,d", "end order");
            Assert(TodoOrder.MovePending(state, "inbox", "d", "b", false), "move to the beginning");
            Assert(Order(state) == "d,b,a,c", "beginning order");
        }

        private static void NoOp()
        {
            TodoState state = State();
            List<TodoItem> list = state.Items;
            TodoItem[] before = state.Items.ToArray();
            Assert(!TodoOrder.MovePending(state, "inbox", "a", "b", false), "already before adjacent target");
            Assert(!TodoOrder.MovePending(state, "inbox", "b", "a", true), "already after adjacent target");
            Assert(!TodoOrder.MovePending(state, "inbox", "b", "b", false), "self move");
            Assert(Object.ReferenceEquals(list, state.Items), "list was replaced");
            Assert(before.SequenceEqual(state.Items), "no-op changed item references/order");
        }

        private static void RejectInvalidTargets()
        {
            TodoState state = State();
            TodoItem[] before = state.Items.ToArray();
            string[] invalidIds = { "x", "done", "deleted", "missing", "", null };
            foreach (string id in invalidIds)
            {
                Assert(!TodoOrder.MovePending(state, "inbox", "a", id, false), "accepted invalid target " + id);
                Assert(!TodoOrder.MovePending(state, "inbox", id, "a", true), "accepted invalid source " + id);
            }
            Assert(!TodoOrder.MovePending(state, "work", "a", "b", false), "accepted wrong category");
            Assert(!TodoOrder.MovePending(null, "inbox", "a", "b", false), "accepted null state");
            Assert(!TodoOrder.MovePending(state, "", "a", "b", false), "accepted empty category");
            Assert(before.SequenceEqual(state.Items), "rejected move changed state");
        }

        private static void PreserveSlots()
        {
            TodoState state = State();
            List<TodoItem> list = state.Items;
            TodoItem[] before = state.Items.ToArray();
            Assert(TodoOrder.MovePending(state, "inbox", "d", "a", false), "valid move failed");
            Assert(Object.ReferenceEquals(list, state.Items), "list instance changed");
            foreach (int index in new int[] { 1, 2, 4, 6 })
                Assert(Object.ReferenceEquals(before[index], state.Items[index]), "unrelated slot moved: " + index);
            Assert(state.Items.Count == before.Length && before.All(x => state.Items.Count(y => Object.ReferenceEquals(x, y)) == 1), "an item was lost or duplicated");
            Assert(Order(state) == "d,a,b,c", "pending order incorrect");
        }

        private static void Persistence()
        {
            string root = Path.GetFullPath(Path.Combine("work", "todo-order-tests-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(root);
            TodoRepository repository = new TodoRepository(root);
            repository.Load(Now);
            TodoState fixture = State();
            repository.State.Categories = fixture.Categories;
            repository.State.Items = fixture.Items;
            Assert(TodoOrder.MovePending(repository.State, "inbox", "d", "b", false), "move was rejected");
            string expected = String.Join(",", repository.State.Items.Select(x => x.Id));
            repository.Save();
            TodoRepository reopened = new TodoRepository(root);
            reopened.Load(Now);
            Assert(String.Join(",", reopened.State.Items.Select(x => x.Id)) == expected, "Save/Load reordered slots");
            Assert(Order(reopened.State) == "a,d,b,c", "saved pending order changed");
            Console.WriteLine("Evidence: " + root);
        }
    }
}
