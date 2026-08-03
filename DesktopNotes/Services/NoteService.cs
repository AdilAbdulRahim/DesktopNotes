using DesktopNotes.Models;

namespace DesktopNotes.Services;

public class NoteService
{
    public List<NoteItem> GetSampleNotes() =>
    [
        new NoteItem { Title = "Idea board", Content = "Drop a thought here", IsCompleted = false },
        new NoteItem { Title = "Focus list", Content = "Prioritize your current tasks", IsCompleted = false }
    ];
}
