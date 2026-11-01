using System;
using System.Globalization;

namespace DesktopTodo
{
    public static class Deadline
    {
        public static DateTimeOffset? Parse(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            DateTimeOffset result;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return result;
            return null;
        }

        public static int Stage(TodoItem item, DateTimeOffset now)
        {
            if (item == null || !String.IsNullOrEmpty(item.CompletedAt) || !String.IsNullOrEmpty(item.DeletedAt)) return 0;
            DateTimeOffset? due = Parse(item.DueAt);
            if (!due.HasValue) return 0;
            TimeSpan remaining = due.Value - now;
            if (remaining <= TimeSpan.Zero) return 3;
            if (remaining <= TimeSpan.FromHours(1)) return 2;
            if (remaining <= TimeSpan.FromHours(24)) return 1;
            return 0;
        }

        public static string Format(TodoItem item, DateTimeOffset now)
        {
            if (item == null) return String.Empty;
            DateTimeOffset? due = Parse(item.DueAt);
            if (!due.HasValue) return String.Empty;
            TimeSpan remaining = due.Value - now;
            bool overdue = remaining <= TimeSpan.Zero;
            double minutes = Math.Abs(remaining.TotalMinutes);
            if (minutes < 1) return overdue ? "刚刚到期" : "不足 1 分钟";
            long total = overdue ? (long)Math.Floor(minutes) : (long)Math.Ceiling(minutes);
            long days = total / 1440;
            long hours = (total % 1440) / 60;
            long mins = total % 60;
            string duration;
            if (days > 0) duration = days + " 天" + (hours > 0 ? " " + hours + " 小时" : String.Empty);
            else if (hours > 0) duration = hours + " 小时" + (mins > 0 ? " " + mins + " 分" : String.Empty);
            else duration = total + " 分钟";
            return (overdue ? "逾期 " : "剩余 ") + duration;
        }

        public static string Mood(int stage)
        {
            switch (stage)
            {
                case 1: return "DDL 快来了，先动一小步？";
                case 2: return "时间开始倒数，我替你捏把汗。";
                case 3: return "DDL 到站了，现在做还是改个时间？";
                default: return "慢慢来，今天先完成一件。";
            }
        }

        public static string Face(int stage)
        {
            switch (stage)
            {
                case 1: return "·_·";
                case 2: return "•﹏•";
                case 3: return "；_；";
                default: return "·ᴗ·";
            }
        }
    }
}
