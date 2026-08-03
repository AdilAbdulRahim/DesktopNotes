using System.IO;
using System.Text.Json;
using DesktopNotes.Models;

namespace DesktopNotes.Services;

public class StorageService
{
    private readonly string _storagePath;
    private readonly string _strokesPath;

    public StorageService()
    {
        var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopNotes");
        Directory.CreateDirectory(appFolder);
        _storagePath = Path.Combine(appFolder, "notes.json");
        _strokesPath = Path.Combine(appFolder, "strokes.isf");
    }

    public List<NoteState> LoadNotes()
    {
        if (!File.Exists(_storagePath))
        {
            return new List<NoteState>();
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            return JsonSerializer.Deserialize<List<NoteState>>(json) ?? new List<NoteState>();
        }
        catch
        {
            return new List<NoteState>();
        }
    }

    public void SaveNotes(IEnumerable<NoteState> notes)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(notes, options);
        File.WriteAllText(_storagePath, json);
    }

    public void SaveInk(byte[] data)
    {
        try
        {
            File.WriteAllBytes(_strokesPath, data);
        }
        catch
        {
            // ignore failures
        }
    }

    public byte[]? LoadInk()
    {
        try
        {
            if (!File.Exists(_strokesPath)) return null;
            return File.ReadAllBytes(_strokesPath);
        }
        catch
        {
            return null;
        }
    }
}
