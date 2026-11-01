using System;
using System.Collections.Generic;

namespace DesktopTodo
{
    /// <summary>Reorders only the unfinished slots of one category.</summary>
    public static class TodoOrder
    {
        public static bool MovePending(TodoState state, string categoryId, string movingId, string targetId, bool insertAfter)
        {
            if (state == null || state.Items == null || String.IsNullOrWhiteSpace(categoryId) ||
                String.IsNullOrWhiteSpace(movingId) || String.IsNullOrWhiteSpace(targetId) ||
                String.Equals(movingId, targetId, StringComparison.Ordinal))
                return false;

            List<int> slots = new List<int>();
            List<TodoItem> pending = new List<TodoItem>();
            int moving = -1, target = -1;
            for (int i = 0; i < state.Items.Count; i++)
            {
                TodoItem item = state.Items[i];
                if (item == null || !String.Equals(item.CategoryId, categoryId, StringComparison.Ordinal) ||
                    !String.IsNullOrEmpty(item.CompletedAt) || !String.IsNullOrEmpty(item.DeletedAt))
                    continue;

                int position = pending.Count;
                if (String.Equals(item.Id, movingId, StringComparison.Ordinal))
                {
                    if (moving >= 0) return false;
                    moving = position;
                }
                if (String.Equals(item.Id, targetId, StringComparison.Ordinal))
                {
                    if (target >= 0) return false;
                    target = position;
                }
                slots.Add(i);
                pending.Add(item);
            }
            if (moving < 0 || target < 0) return false;

            TodoItem movedItem = pending[moving];
            pending.RemoveAt(moving);
            if (moving < target) target--;
            int destination = target + (insertAfter ? 1 : 0);
            if (destination == moving) return false;
            pending.Insert(destination, movedItem);

            // Completed/deleted items and other categories keep their original slots.
            // Keeping the list instance also preserves bindings to the state object.
            for (int i = 0; i < slots.Count; i++) state.Items[slots[i]] = pending[i];
            return true;
        }
    }
}
