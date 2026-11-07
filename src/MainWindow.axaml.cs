using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;

namespace VokoAvalonia;

/// <summary>
/// What the main window does. The layout is in MainWindow.axaml.
///
/// Flow: settings are shown in the controls when the window opens,
/// read back from the controls when recording starts, and saved when the app closes.
/// </summary>
public partial class MainWindow : AppWindow
{
    // Choices shown in the drop-down lists: (text the user sees, value saved in settings).
    // To add a choice, add a line here.
    static readonly (string Label, string Value)[] FormatOptions =
    {
        ("MKV", "mkv"),
        ("MP4", "mp4"),
    };
    static readonly (string Label, int Value)[] FpsOptions =
    {
        ("15 fps", 15), ("24 fps", 24), ("30 fps", 30), ("60 fps", 60),
    };
    static readonly (string Label, int Value)[] QualityOptions =
    {
        ("High", 18), ("Normal", 23), ("Small file", 28),   // value = x264 CRF
    };
    static readonly (string Label, string Value)[] ThemeOptions =
    {
        ("Same as Windows", "System"), ("Light", "Light"), ("Dark", "Dark"),
    };
    static readonly (string Label, string Value)[] LetterOptions =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => (c.ToString(), c.ToString())).ToArray();

    readonly Settings settings = Settings.Load();
    readonly Recorder recorder = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    Hotkeys? hotkeys;
    TimeSpan recordedTime;
    bool isSaving;

    public MainWindow()
    {
        InitializeComponent();
        App.SetTheme(settings.Theme);
        ShowSettings();

        clock.Tick += (_, _) => OnClockTick();
        Opened += OnOpened;
        Closing += OnClosing;
    }


    // ------------------------------------------------------------------
    // Window open / close
    // ------------------------------------------------------------------

    async void OnOpened(object? sender, EventArgs e)
    {
        // The list of screens and the window handle only exist once the window is open.
        ScreenBox.ItemsSource = Screens.All
            .Select((s, i) => $"Display {i + 1} · {s.Bounds.Width} × {s.Bounds.Height}" + (s.IsPrimary ? " (main)" : ""))
            .ToList();
        ScreenBox.SelectedIndex = Math.Clamp(settings.ScreenIndex, 0, Math.Max(0, Screens.All.Count - 1));

        hotkeys = new Hotkeys(this);
        RegisterHotkeys();

        if (Recorder.FindFfmpeg() == null)
        {
            ShowMessage("ffmpeg is missing",
                "This app records with ffmpeg. Put ffmpeg.exe in the same folder as VokoAvalonia.exe, then restart.",
                InfoBarSeverity.Warning);
            MicBox.PlaceholderText = "Needs ffmpeg";
            return;
        }

        // Looking for microphones takes a moment, so do it in the background.
        List<string> microphones = await Task.Run(Recorder.ListMicrophones);
        MicBox.ItemsSource = microphones;
        MicBox.SelectedIndex = Math.Max(0, microphones.IndexOf(settings.Microphone));
        MicBox.PlaceholderText = microphones.Count == 0 ? "No microphone found" : "";
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Closing while recording: save the video before the app quits.
        if (recorder.IsRecording)
        {
            try { recorder.Stop(); }
            catch (Exception) { /* the pieces stay in the temp folder; see Recorder.Stop */ }
        }
        hotkeys?.Clear();
        ReadSettings();
        settings.Save();
    }


    // ------------------------------------------------------------------
    // Settings <-> controls
    // ------------------------------------------------------------------

    /// <summary>Puts the saved settings into the controls.</summary>
    void ShowSettings()
    {
        CursorSwitch.IsChecked = settings.RecordCursor;
        MinimizeSwitch.IsChecked = settings.MinimizeOnStart;
        MicSwitch.IsChecked = settings.RecordMicrophone;
        FolderBox.Text = settings.OutputFolder;

        Fill(FormatBox, FormatOptions, settings.Format);
        Fill(FpsBox, FpsOptions, settings.Fps);
        Fill(QualityBox, QualityOptions, settings.Quality);
        Fill(ThemeBox, ThemeOptions, settings.Theme);
        Fill(RecordKeyBox, LetterOptions, settings.RecordKey);
        Fill(StopKeyBox, LetterOptions, settings.StopKey);
        Fill(SnapshotKeyBox, LetterOptions, settings.SnapshotKey);

        // These take effect right away, so listen for changes (added after filling,
        // so filling the lists above does not trigger them).
        ThemeBox.SelectionChanged += (_, _) => App.SetTheme(Selected(ThemeBox, ThemeOptions));
        RecordKeyBox.SelectionChanged += (_, _) => RegisterHotkeys();
        StopKeyBox.SelectionChanged += (_, _) => RegisterHotkeys();
        SnapshotKeyBox.SelectionChanged += (_, _) => RegisterHotkeys();
    }

    /// <summary>Copies what the user chose in the controls into the settings.</summary>
    void ReadSettings()
    {
        settings.ScreenIndex = Math.Max(0, ScreenBox.SelectedIndex);
        settings.RecordCursor = CursorSwitch.IsChecked == true;
        settings.MinimizeOnStart = MinimizeSwitch.IsChecked == true;
        settings.RecordMicrophone = MicSwitch.IsChecked == true;
        settings.Microphone = MicBox.SelectedItem as string ?? "";
        settings.OutputFolder = FolderBox.Text ?? settings.OutputFolder;
        settings.Format = Selected(FormatBox, FormatOptions);
        settings.Fps = Selected(FpsBox, FpsOptions);
        settings.Quality = Selected(QualityBox, QualityOptions);
        settings.Theme = Selected(ThemeBox, ThemeOptions);
        settings.RecordKey = Selected(RecordKeyBox, LetterOptions);
        settings.StopKey = Selected(StopKeyBox, LetterOptions);
        settings.SnapshotKey = Selected(SnapshotKeyBox, LetterOptions);
    }

    static void Fill<T>(ComboBox box, (string Label, T Value)[] options, T current)
    {
        box.ItemsSource = options.Select(o => o.Label).ToList();
        int index = Array.FindIndex(options, o => Equals(o.Value, current));
        box.SelectedIndex = Math.Max(0, index);
    }

    static T Selected<T>(ComboBox box, (string Label, T Value)[] options) =>
        options[Math.Max(0, box.SelectedIndex)].Value;

    /// <summary>The rectangle of the display chosen in the list.</summary>
    ScreenArea SelectedArea()
    {
        var bounds = Screens.All[Math.Clamp(ScreenBox.SelectedIndex, 0, Math.Max(0, Screens.All.Count - 1))].Bounds;
        return new ScreenArea(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }


    // ------------------------------------------------------------------
    // Recording
    // ------------------------------------------------------------------

    void StartRecording()
    {
        if (recorder.IsRecording || isSaving) return;

        ReadSettings();
        if (settings.RecordMicrophone && settings.Microphone == "")
        {
            ShowMessage("No microphone selected", "Pick a microphone, or turn off Record microphone.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            recorder.Start(settings, SelectedArea());
        }
        catch (Exception ex)
        {
            ShowMessage("Could not start recording", ex.Message, InfoBarSeverity.Error);
            return;
        }

        MessageBar.IsOpen = false;
        recordedTime = TimeSpan.Zero;
        TimeText.Text = "00:00:00";
        StatusText.Text = "Recording…";
        clock.Start();
        UpdateButtons();

        if (settings.MinimizeOnStart) WindowState = WindowState.Minimized;
    }

    void TogglePause()
    {
        if (!recorder.IsRecording) return;

        if (recorder.IsPaused) recorder.Resume();
        else recorder.Pause();

        StatusText.Text = recorder.IsPaused ? "Paused" : "Recording…";
        UpdateButtons();
    }

    async void StopRecording()
    {
        if (!recorder.IsRecording) return;

        clock.Stop();
        isSaving = true;
        StatusText.Text = "Saving…";
        UpdateButtons();

        try
        {
            // Joining the pieces can take a few seconds; keep the window responsive.
            string file = await Task.Run(recorder.Stop);
            StatusText.Text = "Saved: " + Path.GetFileName(file);
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            ShowMessage("Could not save the video", ex.Message, InfoBarSeverity.Error);
        }

        isSaving = false;
        UpdateButtons();
    }

    async void TakeSnapshot()
    {
        ReadSettings();

        // Hide this window for a moment so it is not in the picture.
        bool wasOpen = WindowState != WindowState.Minimized;
        if (wasOpen)
        {
            WindowState = WindowState.Minimized;
            await Task.Delay(400);   // give Windows time to finish the minimize animation
        }

        try
        {
            string file = await Task.Run(() => Recorder.TakeSnapshot(settings, SelectedArea()));
            StatusText.Text = "Snapshot saved: " + Path.GetFileName(file);
        }
        catch (Exception ex)
        {
            ShowMessage("Could not take the snapshot", ex.Message, InfoBarSeverity.Error);
        }

        if (wasOpen) WindowState = WindowState.Normal;
    }

    void OnClockTick()
    {
        // ffmpeg stopped by itself (wrong microphone, missing codec...)? Save what we can and show why.
        string? failure = recorder.FailureMessage;
        if (failure != null)
        {
            clock.Stop();
            try { recorder.Stop(); }
            catch (Exception) { /* nothing usable was recorded */ }

            StatusText.Text = "";
            WindowState = WindowState.Normal;
            ShowMessage("Recording stopped", failure, InfoBarSeverity.Error);
            UpdateButtons();
            return;
        }

        if (!recorder.IsPaused) recordedTime += TimeSpan.FromSeconds(1);
        TimeText.Text = recordedTime.ToString(@"hh\:mm\:ss");
    }

    void UpdateButtons()
    {
        StartButton.IsEnabled = !recorder.IsRecording && !isSaving;
        PauseButton.IsEnabled = recorder.IsRecording;
        StopButton.IsEnabled = recorder.IsRecording;

        PauseText.Text = recorder.IsPaused ? "Continue" : "Pause";
        PauseIcon.Data = (Geometry)this.FindResource(recorder.IsPaused ? "IconPlay" : "IconPause")!;
    }


    // ------------------------------------------------------------------
    // Shortcuts
    // ------------------------------------------------------------------

    void RegisterHotkeys()
    {
        if (hotkeys == null || !OperatingSystem.IsWindows()) return;

        hotkeys.Clear();
        var failed = new List<string>();

        string record = Selected(RecordKeyBox, LetterOptions);
        string stop = Selected(StopKeyBox, LetterOptions);
        string snapshot = Selected(SnapshotKeyBox, LetterOptions);

        // Ctrl+Alt+R: start, or pause / continue if already recording.
        if (!hotkeys.Add(record, () => { if (recorder.IsRecording) TogglePause(); else StartRecording(); }))
            failed.Add(record);
        if (!hotkeys.Add(stop, StopRecording))
            failed.Add(stop);
        if (!hotkeys.Add(snapshot, TakeSnapshot))
            failed.Add(snapshot);

        if (failed.Count > 0)
            ShowMessage("Shortcut not available",
                "Ctrl + Alt + " + string.Join(", ", failed) +
                " is used by another app or picked twice. Choose another letter.",
                InfoBarSeverity.Warning);
    }


    // ------------------------------------------------------------------
    // Buttons (connected in MainWindow.axaml with Click="...")
    // ------------------------------------------------------------------

    void OnStartClick(object? sender, RoutedEventArgs e) => StartRecording();
    void OnPauseClick(object? sender, RoutedEventArgs e) => TogglePause();
    void OnStopClick(object? sender, RoutedEventArgs e) => StopRecording();
    void OnSnapshotClick(object? sender, RoutedEventArgs e) => TakeSnapshot();

    async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Save videos to", AllowMultiple = false });

        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) FolderBox.Text = path;
    }

    void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        string folder = FolderBox.Text ?? "";
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });   // opens File Explorer
    }


    // ------------------------------------------------------------------

    void ShowMessage(string title, string message, InfoBarSeverity severity)
    {
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.Severity = severity;
        MessageBar.IsOpen = true;
    }
}
