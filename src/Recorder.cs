using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VokoAvalonia;

/// <summary>A rectangle on the desktop, in physical pixels.</summary>
public record ScreenArea(int X, int Y, int Width, int Height);

/// <summary>
/// Records the screen by running ffmpeg as a separate program.
/// This class only builds ffmpeg command lines and starts / stops ffmpeg.
///
/// How pause works: ffmpeg cannot pause, so we record in pieces ("segments").
///   Pause    = finish the current segment.
///   Continue = start a new segment.
///   Stop     = finish the last segment and join all segments into one file.
/// </summary>
public class Recorder
{
    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }

    Process? ffmpeg;                         // the ffmpeg that is recording right now
    readonly StringBuilder ffmpegErrors = new();
    readonly List<string> segments = new();
    Settings settings = new();
    ScreenArea area = new(0, 0, 0, 0);

    static string TempFolder => Path.Combine(Path.GetTempPath(), "VokoAvalonia");


    // ------------------------------------------------------------------
    // Recording
    // ------------------------------------------------------------------

    public void Start(Settings settings, ScreenArea area)
    {
        if (IsRecording) return;

        this.settings = settings;
        this.area = area;

        // Start with an empty temp folder for this recording's segments.
        if (Directory.Exists(TempFolder)) Directory.Delete(TempFolder, recursive: true);
        Directory.CreateDirectory(TempFolder);
        segments.Clear();

        StartSegment();
        IsRecording = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        StopSegment();
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        StartSegment();
        IsPaused = false;
    }

    /// <summary>
    /// Stops recording and joins the segments into one video.
    /// Returns the path of the finished file. Can take a few seconds.
    /// </summary>
    public string Stop()
    {
        if (!IsRecording) throw new InvalidOperationException("Not recording.");

        if (!IsPaused) StopSegment();
        IsRecording = false;
        IsPaused = false;

        Directory.CreateDirectory(settings.OutputFolder);
        string finalFile = Path.Combine(settings.OutputFolder, NewFileName(settings.Format));

        // ffmpeg's "concat" reads a text file listing the pieces to join, in order.
        // "-c copy" joins them without re-encoding, so this is fast.
        string listFile = Path.Combine(TempFolder, "segments.txt");
        File.WriteAllLines(listFile, segments
            .Where(File.Exists)
            .Select(s => "file '" + s.Replace("'", "'\\''") + "'"));

        var (exitCode, errors) = RunAndWait(
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "concat", "-safe", "0", "-i", listFile,
            "-c", "copy", finalFile);

        if (exitCode != 0 || !File.Exists(finalFile))
            throw new Exception("Could not save the video. Your recording pieces are still in "
                                + TempFolder + "\n\n" + errors);

        Directory.Delete(TempFolder, recursive: true);
        return finalFile;
    }

    /// <summary>
    /// If ffmpeg quit by itself while recording (wrong device, missing codec...),
    /// returns its error text. Otherwise returns null.
    /// </summary>
    public string? FailureMessage
    {
        get
        {
            if (!IsRecording || IsPaused || ffmpeg == null || !ffmpeg.HasExited) return null;
            lock (ffmpegErrors)
                return ffmpegErrors.Length > 0 ? ffmpegErrors.ToString() : "ffmpeg stopped unexpectedly.";
        }
    }

    void StartSegment()
    {
        string segment = Path.Combine(TempFolder, $"segment{segments.Count + 1}.mkv");

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        args.AddRange(ScreenInput(settings, area));
        if (settings.RecordMicrophone)
            args.AddRange(MicrophoneInput(settings.Microphone));

        // Video: H.264, widely playable. "veryfast" keeps CPU use low while recording.
        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast",
                              "-crf", settings.Quality.ToString(), "-pix_fmt", "yuv420p" });
        if (settings.RecordMicrophone)
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "160k" });

        // Segments are always MKV: it stays readable even if the app crashes mid-recording.
        args.Add(segment);

        lock (ffmpegErrors) ffmpegErrors.Clear();
        ffmpeg = StartFfmpeg(args);
        segments.Add(segment);
    }

    void StopSegment()
    {
        if (ffmpeg == null) return;

        // Typing "q" is how you ask ffmpeg to finish the file properly.
        if (!ffmpeg.HasExited)
        {
            try { ffmpeg.StandardInput.Write("q"); ffmpeg.StandardInput.Flush(); }
            catch (IOException) { /* ffmpeg already closed its input */ }
        }
        if (!ffmpeg.WaitForExit(15000)) ffmpeg.Kill();

        ffmpeg.Dispose();
        ffmpeg = null;
    }


    // ------------------------------------------------------------------
    // Snapshot
    // ------------------------------------------------------------------

    /// <summary>Saves a PNG of the screen area. Returns the file path.</summary>
    public static string TakeSnapshot(Settings settings, ScreenArea area)
    {
        Directory.CreateDirectory(settings.OutputFolder);
        string file = Path.Combine(settings.OutputFolder, NewFileName("png"));

        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        args.AddRange(ScreenInput(settings, area));
        args.AddRange(new[] { "-frames:v", "1", file });

        var (exitCode, errors) = RunAndWait(args.ToArray());
        if (exitCode != 0 || !File.Exists(file))
            throw new Exception("Could not take the snapshot.\n\n" + errors);
        return file;
    }


    // ------------------------------------------------------------------
    // Devices
    // ------------------------------------------------------------------

    /// <summary>Names of the microphones ffmpeg can record from.</summary>
    public static List<string> ListMicrophones()
    {
        if (!OperatingSystem.IsWindows())
            return new List<string> { "default" };   // Linux: PulseAudio / PipeWire default input

        // ffmpeg prints devices like:  [dshow @ 0000...] "Microphone (Realtek Audio)" (audio)
        var (_, output) = RunAndWait("-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy");
        return Regex.Matches(output, "\"([^\"]+)\"\\s+\\(audio\\)")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .ToList();
    }


    // ------------------------------------------------------------------
    // ffmpeg command-line pieces. Add other platforms or sources here.
    // ------------------------------------------------------------------

    static string[] ScreenInput(Settings s, ScreenArea a)
    {
        // H.264 needs even width and height.
        int width = a.Width - a.Width % 2;
        int height = a.Height - a.Height % 2;
        string fps = s.Fps.ToString();
        string cursor = s.RecordCursor ? "1" : "0";

        if (OperatingSystem.IsWindows())
            return new[] { "-f", "gdigrab", "-framerate", fps, "-draw_mouse", cursor,
                           "-offset_x", a.X.ToString(), "-offset_y", a.Y.ToString(),
                           "-video_size", $"{width}x{height}", "-i", "desktop" };

        // Linux with X11 (handy for development).
        string display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
        return new[] { "-f", "x11grab", "-framerate", fps, "-draw_mouse", cursor,
                       "-video_size", $"{width}x{height}", "-i", $"{display}+{a.X},{a.Y}" };
    }

    static string[] MicrophoneInput(string device)
    {
        if (OperatingSystem.IsWindows())
            return new[] { "-thread_queue_size", "1024", "-f", "dshow", "-i", "audio=" + device };

        return new[] { "-thread_queue_size", "1024", "-f", "pulse", "-i", "default" };
    }

    static string NewFileName(string extension) =>
        $"VokoAvalonia_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.{extension}";


    // ------------------------------------------------------------------
    // Running ffmpeg
    // ------------------------------------------------------------------

    /// <summary>Finds ffmpeg next to this app first, then in the system PATH.</summary>
    public static string? FindFfmpeg()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        string nextToApp = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(nextToApp)) return nextToApp;

        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder.Trim(), exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static ProcessStartInfo FfmpegStartInfo(IEnumerable<string> args)
    {
        string ffmpegPath = FindFfmpeg() ?? throw new FileNotFoundException(
            "ffmpeg was not found. Put ffmpeg.exe in the same folder as VokoAvalonia.exe.");

        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,          // no black console window
            RedirectStandardError = true,   // ffmpeg writes its messages here
        };
        foreach (string arg in args) info.ArgumentList.Add(arg);   // handles spaces and quotes for us
        return info;
    }

    /// <summary>Starts ffmpeg and keeps it running (for recording).</summary>
    Process StartFfmpeg(IEnumerable<string> args)
    {
        var info = FfmpegStartInfo(args);
        info.RedirectStandardInput = true;  // so we can send "q" to stop

        var process = new Process { StartInfo = info };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) lock (ffmpegErrors) ffmpegErrors.AppendLine(e.Data);
        };
        process.Start();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>Runs ffmpeg, waits until it is done, returns its exit code and messages.</summary>
    static (int ExitCode, string Output) RunAndWait(params string[] args)
    {
        using var process = Process.Start(FfmpegStartInfo(args))!;
        string output = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
