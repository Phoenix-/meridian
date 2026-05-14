using System.Diagnostics;
using System.Text;

namespace Meridian.Diagnostics;

// Single-file rotating text log for the whole app. Writes are append-only,
// timestamped, lock-serialized, and non-throwing — IO failures are swallowed
// because losing a log line must never break the feature being logged.
//
// File layout under %APPDATA%\Meridian\logs\:
//   meridian.log    — current
//   meridian.log.1  — previous generation
//   meridian.log.2  — generation before that
// When meridian.log exceeds MaxBytes on a write, the chain is shifted down
// (.2 dropped, .1 → .2, current → .1) and writing continues into a fresh file.
//
// Each line format:
//   2026-05-14 18:23:45.123 [Category] message text
// Categories are free-form strings; pick something short and grep-friendly.
//
// Mirrors every line to Debug.WriteLine so the VS Output window still works
// during interactive debugging — no need to maintain two logging paths.
public static class Log
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const int Generations = 2;

    private static readonly Lock _gate = new();
    private static string? _filePath;

    private static string FilePath
    {
        get
        {
            if (_filePath != null) return _filePath;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Meridian", "logs");
            try { Directory.CreateDirectory(dir); } catch { /* swallowed below */ }
            _filePath = Path.Combine(dir, "meridian.log");
            return _filePath;
        }
    }

    public static void Write(string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
        Debug.WriteLine(line);
        AppendLine(line);
    }

    public static void Error(string category, Exception ex, string? context = null)
    {
        var prefix = context is null ? "EX:" : $"EX: {context}:";
        // ex.ToString() includes type, message, and full stack — keep it as
        // one logical record so grepping by stack trace still finds the head.
        Write(category, $"{prefix} {ex}");
    }

    private static void AppendLine(string line)
    {
        try
        {
            lock (_gate)
            {
                var path = FilePath;
                RotateIfNeeded(path, line.Length);
                using var stream = new FileStream(
                    path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, Encoding.UTF8);
                writer.WriteLine(line);
            }
        }
        catch
        {
            // Logger must never throw. Losing a line is acceptable; losing the
            // feature that called us is not.
        }
    }

    private static void RotateIfNeeded(string path, int incomingLineBytes)
    {
        try
        {
            if (!File.Exists(path)) return;
            var size = new FileInfo(path).Length;
            // Rotate BEFORE the write that would exceed the budget rather than
            // after — keeps the current file strictly under MaxBytes so callers
            // tailing it don't see brief overshoots.
            if (size + incomingLineBytes <= MaxBytes) return;

            // Shift: .2 → drop, .1 → .2, current → .1
            for (int i = Generations; i >= 1; i--)
            {
                var older = $"{path}.{i}";
                var newer = i == 1 ? path : $"{path}.{i - 1}";
                if (!File.Exists(newer)) continue;
                if (File.Exists(older))
                {
                    try { File.Delete(older); } catch { }
                }
                try { File.Move(newer, older); } catch { }
            }
        }
        catch { /* swallowed */ }
    }
}
