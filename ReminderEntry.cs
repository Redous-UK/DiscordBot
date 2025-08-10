
using System;

public class ReminderEntry
{
    public string Message { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public bool IsRepeating { get; set; }
    public string Interval { get; set; } = string.Empty;
}
