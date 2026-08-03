using System.Collections.ObjectModel;
using DesktopNotes.Models;

namespace DesktopNotes.ViewModels;

public class MainViewModel
{
    public ObservableCollection<NoteItem> Notes { get; } = new()
    {
        new NoteItem { Title = "Morning ideas", Content = "Capture quick thoughts here", IsCompleted = false },
        new NoteItem { Title = "Today", Content = "Plan your priorities", IsCompleted = false }
    };
}
