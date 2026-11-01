using System;
using System.Collections.Generic;

namespace DesktopTodo
{
    public sealed class TodoState
    {
        public int Version { get; set; }
        public List<TodoCategory> Categories { get; set; }
        public List<TodoItem> Items { get; set; }
        public AppSettings Settings { get; set; }

        public TodoState()
        {
            Version = 1;
            Categories = new List<TodoCategory>();
            Items = new List<TodoItem>();
            Settings = new AppSettings();
        }
    }

    public sealed class TodoCategory
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class TodoItem
    {
        public string Id { get; set; }
        public string CategoryId { get; set; }
        public string Title { get; set; }
        public string CreatedAt { get; set; }
        public string DueAt { get; set; }
        public string CompletedAt { get; set; }
        public string DeletedAt { get; set; }
    }

    public sealed class AppSettings
    {
        public double BackgroundOpacity { get; set; }
        public bool Topmost { get; set; }
        public bool Animations { get; set; }
        public bool StartWithWindows { get; set; }
        public string Theme { get; set; }
        public string ActiveCategoryId { get; set; }
        public bool ShowCompleted { get; set; }

        public AppSettings()
        {
            BackgroundOpacity = 0.35;
            Animations = true;
            Theme = "auto";
            ActiveCategoryId = "inbox";
        }
    }

    public sealed class ArchiveDocument
    {
        public int Version { get; set; }
        public string Date { get; set; }
        public List<ArchivedTodo> Items { get; set; }

        public ArchiveDocument()
        {
            Version = 1;
            Items = new List<ArchivedTodo>();
        }
    }

    public sealed class ArchivedTodo
    {
        public string Id { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string Title { get; set; }
        public string CreatedAt { get; set; }
        public string DueAt { get; set; }
        public string CompletedAt { get; set; }
        public string DeletedAt { get; set; }
    }
}
