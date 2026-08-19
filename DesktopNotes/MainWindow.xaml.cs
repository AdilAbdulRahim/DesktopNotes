using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using DesktopNotes.Models;
using DesktopNotes.Services;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace DesktopNotes;

public partial class MainWindow : Window
{
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int WM_SETTINGCHANGE = 0x001A;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private WpfPoint _lastClickPosition;
    private Forms.NotifyIcon? _notifyIcon;
    private Drawing.Icon? _appTrayIcon;
    private double _wallpaperLuminance = -1;
    private HwndSource? _source;
    private DispatcherTimer? _bottomTimer;
    private bool _isDrawingMode = false;
    private readonly StorageService _storageService = new StorageService();
    // redo stacks: store recently undone strokes and removed notes for redo
    private readonly System.Collections.Generic.Stack<System.Windows.Ink.Stroke> _redoStrokes = new();
    private readonly System.Collections.Generic.Stack<(double X, double Y, string Text)> _redoNotes = new();
    private const byte DefaultCanvasAlpha = 0x03; // 1% alpha for overlay effect
    private byte _currentCanvasAlpha = DefaultCanvasAlpha;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        var appIcon = CreateAppIcon();
        Icon = appIcon.WindowIcon;
        _appTrayIcon = appIcon.TrayIcon;
        SetCanvasBackgroundColor(WpfColor.FromArgb(_currentCanvasAlpha, 0xFF, 0xFF, 0xFF));
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetWindowToWorkArea();
        EnsureTrayIcon();
        RegisterAutoStart();
        UpdateWallpaperLuminance();
        SetupWallpaperChangeHook();
        LoadSavedNotes();
        // capture new strokes to clear redo history when user draws
        DrawCanvas.StrokeCollected += DrawCanvas_StrokeCollected;

        var helper = new WindowInteropHelper(this);
        var hWnd = helper.Handle;
        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
        PushWindowToBack();

        _bottomTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _bottomTimer.Tick += (s, args) => PushWindowToBack();
        _bottomTimer.Start();

        // Start icon exclusion refresh so desktop icons remain clickable
        // icon detection temporarily disabled
    }

    private void SetWindowToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    
    // Desktop icon detection temporarily removed

    private void PushWindowToBack()
    {
        var helper = new WindowInteropHelper(this);
        // Try to place the overlay just above the desktop's list view so it receives clicks
        try
        {
            IntPtr listView = IntPtr.Zero;
            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
                }
            }

            if (listView == IntPtr.Zero)
            {
                // enumerate WorkerW windows to find the listview
                EnumWindows((hwnd, lparam) =>
                {
                    var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (child != IntPtr.Zero)
                    {
                        listView = FindWindowEx(child, IntPtr.Zero, "SysListView32", "FolderView");
                        return false; // stop
                    }

                    return true; // continue
                }, IntPtr.Zero);
            }

            if (listView != IntPtr.Zero)
            {
                SetWindowPos(helper.Handle, listView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return;
            }
        }
        catch
        {
            // fallback
        }

        // default: bottom-most
        SetWindowPos(helper.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // Desktop icon exclusion code removed

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);

    private void EnsureTrayIcon()
    {
        if (_notifyIcon != null)
        {
            return;
        }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _appTrayIcon ?? Drawing.SystemIcons.Application,
            Text = "DesktopNotes",
            Visible = true,
            BalloonTipTitle = "DesktopNotes",
            BalloonTipText = "Your hidden wallpaper notes are running.",
            BalloonTipIcon = Forms.ToolTipIcon.Info
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show notes", null, (s, e) => Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Exit", null, (s, e) => Dispatcher.Invoke(Close));
        _notifyIcon.ContextMenuStrip = menu;
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        PushWindowToBack();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _bottomTimer?.Stop();
        _bottomTimer = null;
        SaveNotes();
    }

    private void SaveNotes()
    {
        var notes = NotesCanvas.Children
            .OfType<WpfTextBox>()
            .Select(textBox => new NoteState
            {
                X = Canvas.GetLeft(textBox),
                Y = Canvas.GetTop(textBox),
                Text = textBox.Text
            })
            .Where(note => !string.IsNullOrWhiteSpace(note.Text))
            .ToList();

        _storageService.SaveNotes(notes);
        // save ink strokes
        try
        {
            using var ms = new MemoryStream();
            DrawCanvas.Strokes.Save(ms);
            _storageService.SaveInk(ms.ToArray());
        }
        catch
        {
            // ignore stroke save failures
        }
    }

    private void LoadSavedNotes()
    {
        var savedNotes = _storageService.LoadNotes();

        foreach (var note in savedNotes)
        {
            CreateTextBox(new WpfPoint(note.X, note.Y), note.Text);
        }

        // load ink strokes if present
        try
        {
            var data = _storageService.LoadInk();
            if (data != null && data.Length > 0)
            {
                using var ms = new MemoryStream(data);
                var strokes = new System.Windows.Ink.StrokeCollection(ms);
                DrawCanvas.Strokes = strokes;
            }
        }
        catch
        {
            // ignore
        }
    }

    private void RegisterAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if (key is null)
            {
                return;
            }

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            const string runKeyName = "DesktopNotes";
            var currentValue = key.GetValue(runKeyName) as string;
            if (!string.Equals(currentValue, exePath, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(runKeyName, exePath);
            }
        }
        catch
        {
            // Ignore registry access failures.
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // if in drawing mode, don't create notes
        if (_isDrawingMode)
        {
            return;
        }

        PromptTextBlock.Visibility = Visibility.Collapsed;

        if (e.OriginalSource is WpfTextBox)
        {
            return;
        }

        _lastClickPosition = e.GetPosition(NotesCanvas);
        CreateTextBox(_lastClickPosition);
    }

    private void PencilButton_Click(object sender, RoutedEventArgs e)
    {
        _isDrawingMode = !_isDrawingMode;

        if (_isDrawingMode)
        {
            // enable drawing: make the canvas accept input, set pen tip size and show drawing cursor
            DrawCanvas.IsHitTestVisible = true;
            DrawCanvas.EditingMode = InkCanvasEditingMode.Ink;
            // make the ink tip larger for visibility
            try
            {
                var fgBrush = GetForegroundBrush() as SolidColorBrush;
                if (fgBrush != null)
                {
                    DrawCanvas.DefaultDrawingAttributes.Color = fgBrush.Color;
                }
            }
            catch
            {
                // ignore color extraction failures
            }
            DrawCanvas.DefaultDrawingAttributes.Width = 8;
            DrawCanvas.DefaultDrawingAttributes.Height = 8;
            DrawCanvas.Cursor = System.Windows.Input.Cursors.Pen;
            Cursor = System.Windows.Input.Cursors.Pen;
            PencilIcon.Text = "T"; // switch icon to indicate text-mode available when drawing
        }
        else
        {
            // keep the strokes visible but stop intercepting input so text creation works
            DrawCanvas.IsHitTestVisible = false;
            DrawCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
            Cursor = System.Windows.Input.Cursors.Arrow;
            PencilIcon.Text = "✎";
        }
    }

    private void DrawCanvas_StrokeCollected(object? sender, InkCanvasStrokeCollectedEventArgs e)
    {
        // New stroke invalidates redo history for strokes/notes
        _redoStrokes.Clear();
        _redoNotes.Clear();
        SaveNotes();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        UndoLastAction();
    }

    private void RedoButton_Click(object sender, RoutedEventArgs e)
    {
        RedoLastAction();
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            UndoLastAction();
            e.Handled = true;
        }
        // Ctrl+Y for redo (common Windows binding)
        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            RedoLastAction();
            e.Handled = true;
        }
    }

    private void UndoLastAction()
    {
        if (_isDrawingMode)
        {
            if (DrawCanvas.Strokes.Count > 0)
            {
                var last = DrawCanvas.Strokes[DrawCanvas.Strokes.Count - 1];
                DrawCanvas.Strokes.Remove(last);
                _redoStrokes.Push(last);
                SaveNotes();
            }
            return;
        }

        if (TryUndoFocusedTextBox())
        {
            return;
        }

        if (TryRemoveLastTextBox())
        {
            return;
        }
    }

    private bool TryUndoFocusedTextBox()
    {
        if (Keyboard.FocusedElement is WpfTextBox textBox)
        {
            if (textBox.CanUndo)
            {
                textBox.Undo();
                SaveNotes();
                return true;
            }
        }

        return false;
    }

    private bool TryRemoveLastTextBox()
    {
        for (var index = NotesCanvas.Children.Count - 1; index >= 0; index--)
        {
            if (NotesCanvas.Children[index] is WpfTextBox)
            {
                var tb = (WpfTextBox)NotesCanvas.Children[index];
                var x = Canvas.GetLeft(tb);
                var y = Canvas.GetTop(tb);
                var text = tb.Text;
                NotesCanvas.Children.RemoveAt(index);
                _redoNotes.Push((x, y, text));
                SaveNotes();
                return true;
            }
        }

        return false;
    }

    private void RedoLastAction()
    {
        if (_isDrawingMode)
        {
            if (_redoStrokes.Count > 0)
            {
                var stroke = _redoStrokes.Pop();
                DrawCanvas.Strokes.Add(stroke);
                SaveNotes();
            }
            return;
        }

        if (TryRedoFocusedTextBox())
        {
            return;
        }

        if (TryRestoreLastRemovedTextBox())
        {
            return;
        }
    }

    private bool TryRedoFocusedTextBox()
    {
        if (Keyboard.FocusedElement is WpfTextBox textBox)
        {
            if (textBox.CanRedo)
            {
                textBox.Redo();
                SaveNotes();
                return true;
            }
        }

        return false;
    }

    private bool TryRestoreLastRemovedTextBox()
    {
        if (_redoNotes.Count > 0)
        {
            var note = _redoNotes.Pop();
            CreateTextBox(new WpfPoint(note.X, note.Y), note.Text);
            SaveNotes();
            return true;
        }

        return false;
    }

    private (ImageSource WindowIcon, Drawing.Icon TrayIcon) CreateAppIcon()
    {
        using var bitmap = new Drawing.Bitmap(256, 256);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Drawing.Color.FromArgb(255, 25, 35, 64));

        using var backgroundBrush = new Drawing2D.LinearGradientBrush(
            new Drawing.Rectangle(0, 0, 256, 256),
            Drawing.Color.FromArgb(255, 72, 128, 255),
            Drawing.Color.FromArgb(255, 21, 34, 58),
            45f);
        graphics.FillRectangle(backgroundBrush, 0, 0, 256, 256);

        using var outerBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(240, 255, 255, 255));
        graphics.FillEllipse(outerBrush, 40, 40, 176, 176);
        using var innerBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 53, 99, 255));
        graphics.FillEllipse(innerBrush, 58, 58, 140, 140);

        using var noteBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 255, 255, 255));
        graphics.FillRectangle(noteBrush, 88, 78, 80, 92);
        using var accentPen = new Drawing.Pen(Drawing.Color.FromArgb(255, 53, 99, 255), 12f);
        accentPen.StartCap = Drawing2D.LineCap.Round;
        accentPen.EndCap = Drawing2D.LineCap.Round;
        graphics.DrawLine(accentPen, 102, 110, 150, 110);
        graphics.DrawLine(accentPen, 102, 133, 142, 133);
        graphics.DrawLine(accentPen, 102, 156, 128, 156);

        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImaging.ImageFormat.Png);
        stream.Position = 0;

        var windowIcon = new BitmapImage();
        windowIcon.BeginInit();
        windowIcon.CacheOption = BitmapCacheOption.OnLoad;
        windowIcon.StreamSource = stream;
        windowIcon.EndInit();
        windowIcon.Freeze();

        var trayIcon = Drawing.Icon.FromHandle(bitmap.GetHicon());
        return (windowIcon, trayIcon);
    }

    private void CreateTextBox(WpfPoint position, string initialText = "")
    {
        var textBox = new WpfTextBox
        {
            Background = System.Windows.Media.Brushes.Transparent,
            BorderBrush = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FocusVisualStyle = null,
            Foreground = GetForegroundBrush(),
            FontSize = 20,
            FontFamily = new WpfFontFamily("Segoe UI Variable Text"),
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            MinWidth = 160,
            MinHeight = 40,
            Padding = new Thickness(0),
            CaretBrush = GetForegroundBrush(),
            Text = initialText
        };
        textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
        textBox.LostKeyboardFocus += (s, e) => SaveNotes();
        textBox.TextChanged += (s, e) => SaveNotes();
        textBox.GotKeyboardFocus += (s, e) => PromptTextBlock.Visibility = Visibility.Collapsed;
        textBox.Focusable = true;
        textBox.Cursor = System.Windows.Input.Cursors.IBeam;

        Canvas.SetLeft(textBox, Math.Max(24, position.X));
        Canvas.SetTop(textBox, Math.Max(24, position.Y));

        NotesCanvas.Children.Add(textBox);

        // creating a new note invalidates redo history for removed notes/strokes
        _redoNotes.Clear();
        _redoStrokes.Clear();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
        }));
    }

    private void BulletButton_Click(object sender, RoutedEventArgs e)
    {
        CreateTextBox(_lastClickPosition, "• ");
    }

    private void NumberButton_Click(object sender, RoutedEventArgs e)
    {
        CreateTextBox(_lastClickPosition, "1. ");
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        string? colorText = null;

        if (sender is WpfButton button && button.Tag is string tagText)
        {
            colorText = tagText;
        }
        else if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string menuTag)
        {
            colorText = menuTag;
        }

        if (string.IsNullOrEmpty(colorText))
        {
            return;
        }

        var color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(colorText);
        SetCanvasBackgroundColor(color);
        SaveNotes();
    }

    private void SetCanvasBackgroundColor(WpfColor color)
    {
        // respect the provided alpha so caller can request fully transparent background for testing
        var brush = new SolidColorBrush(WpfColor.FromArgb(color.A, color.R, color.G, color.B));
        brush.Freeze();
        NotesCanvas.Background = brush;
        UpdateTextContrast(color);
    }

    private void TextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.Return || sender is not WpfTextBox textBox)
        {
            return;
        }

        var currentLine = GetCurrentLineText(textBox);
        if (string.IsNullOrWhiteSpace(currentLine))
        {
            return;
        }

        var listPrefix = GetListPrefix(currentLine);
        if (listPrefix is null)
        {
            return;
        }

        e.Handled = true;
        InsertLineBreakWithPrefix(textBox, listPrefix);
    }

    private string GetCurrentLineText(WpfTextBox textBox)
    {
        var caretIndex = textBox.CaretIndex;
        var text = textBox.Text;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caretIndex - 1));
        if (lineStart < 0)
        {
            lineStart = 0;
        }
        else
        {
            lineStart += 1;
        }

        var lineEnd = text.IndexOf('\n', caretIndex);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        return text[lineStart..lineEnd].TrimEnd();
    }

    private string? GetListPrefix(string lineText)
    {
        var numberedMatch = Regex.Match(lineText, @"^(\s*)(\d+)\.(\s*)(.*)$");
        if (numberedMatch.Success)
        {
            var indent = numberedMatch.Groups[1].Value;
            var number = int.Parse(numberedMatch.Groups[2].Value);
            return $"{indent}{number + 1}. ";
        }

        var bulletMatch = Regex.Match(lineText, @"^(\s*)([-*•])(\s+)(.*)$");
        if (bulletMatch.Success)
        {
            var indent = bulletMatch.Groups[1].Value;
            var bullet = bulletMatch.Groups[2].Value;
            return $"{indent}{bullet} ";
        }

        return null;
    }

    private void InsertLineBreakWithPrefix(WpfTextBox textBox, string prefix)
    {
        var caretIndex = textBox.CaretIndex;
        var text = textBox.Text;
        var before = text.Substring(0, caretIndex);
        var after = text.Substring(caretIndex);

        textBox.Text = before + "\n" + prefix + after;
        textBox.CaretIndex = before.Length + 1 + prefix.Length;
    }

    private void UpdateTextContrast(WpfColor backgroundColor)
    {
        var foreground = _wallpaperLuminance < 0 ? GetForegroundBrush(backgroundColor) : GetForegroundBrush(_wallpaperLuminance);

        foreach (var child in NotesCanvas.Children)
        {
            if (child is WpfTextBox textBox)
            {
                textBox.Foreground = foreground;
                textBox.CaretBrush = foreground;
            }
        }
    }

    private WpfBrush GetForegroundBrush()
    {
        return _wallpaperLuminance < 0
            ? GetForegroundBrush((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#CCEBF4FF"))
            : GetForegroundBrush(_wallpaperLuminance);
    }

    private WpfBrush GetForegroundBrush(WpfColor backgroundColor)
    {
        var luminance = 0.2126 * (backgroundColor.R / 255.0) + 0.7152 * (backgroundColor.G / 255.0) + 0.0722 * (backgroundColor.B / 255.0);
        return luminance < 0.5 ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
    }

    private WpfBrush GetForegroundBrush(double wallpaperLuminance)
    {
        return wallpaperLuminance < 0.5 ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Black;
    }

    private void UpdateWallpaperLuminance()
    {
        try
        {
            var sb = new StringBuilder(260);
            if (SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0))
            {
                var wallpaperPath = sb.ToString();
                if (!string.IsNullOrEmpty(wallpaperPath) && File.Exists(wallpaperPath))
                {
                    using var image = System.Drawing.Image.FromFile(wallpaperPath);
                    using var bitmap = new System.Drawing.Bitmap(image, 80, 80);

                    double total = 0;
                    int count = 0;

                    for (int x = 0; x < bitmap.Width; x += 4)
                    {
                        for (int y = 0; y < bitmap.Height; y += 4)
                        {
                            var pixel = bitmap.GetPixel(x, y);
                            total += 0.2126 * (pixel.R / 255.0) + 0.7152 * (pixel.G / 255.0) + 0.0722 * (pixel.B / 255.0);
                            count++;
                        }
                    }

                    if (count > 0)
                    {
                        _wallpaperLuminance = total / count;
                    }
                }
            }
        }
        catch
        {
            _wallpaperLuminance = -1;
        }
    }

    private void SetupWallpaperChangeHook()
    {
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);

        var wallpaperTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        wallpaperTimer.Tick += (s, e) =>
        {
            var oldLuminance = _wallpaperLuminance;
            UpdateWallpaperLuminance();
            if (Math.Abs(oldLuminance - _wallpaperLuminance) > 0.05)
            {
                UpdateTextContrast((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#CCEBF4FF"));
            }
        };
        wallpaperTimer.Start();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SETTINGCHANGE)
        {
            var setting = Marshal.PtrToStringUni(lParam);
            if (string.Equals(setting, "DesktopWallpaper", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(setting))
            {
                UpdateWallpaperLuminance();
                UpdateTextContrast((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString("#CCEBF4FF"));
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, StringBuilder pvParam, int fWinIni);
}
