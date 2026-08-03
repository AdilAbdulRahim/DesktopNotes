namespace DesktopNotes.Models;

public class NoteItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New note";
    public string Content { get; set; } = "";
    public bool IsCompleted { get; set; }
}
