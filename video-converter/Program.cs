using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var repoRoot = FindRepoRoot();
var videosDir = Path.Combine(repoRoot, "videos");
var outputPath = Path.Combine(repoRoot, "docs", "episodes.json");

if (!Directory.Exists(videosDir))
{
    Console.WriteLine($"Videos folder not found at {videosDir}");
    return;
}

var videoFiles = Directory.EnumerateFiles(videosDir)
    .Where(IsVideoFile)
    .OrderBy(f => f)
    .ToList();

if (videoFiles.Count == 0)
{
    Console.WriteLine("No video files found in the videos folder.");
    return;
}

var ffprobePath = FindTool("ffprobe");
var ffmpegPath = FindTool("ffmpeg");
var ffprobeAvailable = ToolAvailable(ffprobePath);
var ffmpegAvailable = ToolAvailable(ffmpegPath);

Console.WriteLine(ffprobeAvailable
    ? $"Using ffprobe at {ffprobePath}"
    : "ffprobe not found (durations will be Unknown)");
Console.WriteLine(ffmpegAvailable
    ? $"Using ffmpeg at {ffmpegPath}"
    : "ffmpeg not found (thumbnails will be empty)");

var episodes = new List<Episode>();
var id = 1;

foreach (var file in videoFiles)
{
    Console.WriteLine($"Processing {Path.GetFileName(file)}...");

    var title = Path.GetFileNameWithoutExtension(file);
    var mime = GetMimeType(Path.GetExtension(file));
    var src = await BuildDataUrlAsync(file, mime);

    TimeSpan? duration = null;
    if (ffprobeAvailable)
    {
        duration = await ProbeDurationAsync(ffprobePath!, file);
    }

    var durationLabel = duration.HasValue ? FormatDuration(duration.Value) : "Unknown";

    string thumbnail = string.Empty;
    if (ffmpegAvailable)
    {
        var thumb = await CaptureThumbnailAsync(ffmpegPath!, file, duration);
        if (!string.IsNullOrWhiteSpace(thumb))
        {
            thumbnail = thumb;
        }
    }

    episodes.Add(new Episode
    {
        Id = id++,
        Title = title,
        Src = src,
        Duration = durationLabel,
        Thumbnail = thumbnail
    });
}

var settings = new JsonSerializerSettings
{
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    Formatting = Formatting.Indented
};

var json = JsonConvert.SerializeObject(episodes, settings);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, json);

Console.WriteLine($"Written {episodes.Count} episodes to {outputPath}");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

static bool IsVideoFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext is ".mp4" or ".mkv" or ".mov" or ".m4v" or ".avi" or ".webm" or ".mpg" or ".mpeg" or ".ts";
}

static string GetMimeType(string extension)
{
    return extension.ToLowerInvariant() switch
    {
        ".webm" => "video/webm",
        ".mkv" => "video/x-matroska",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mpg" or ".mpeg" => "video/mpeg",
        ".ts" => "video/MP2T",
        _ => "video/mp4"
    };
}

static async Task<string> BuildDataUrlAsync(string filePath, string mime)
{
    var bytes = await File.ReadAllBytesAsync(filePath);
    var base64 = Convert.ToBase64String(bytes);
    return $"data:{mime};base64,{base64}";
}

static string FormatDuration(TimeSpan duration)
{
    if (duration.TotalHours >= 1)
    {
        var totalHours = (int)duration.TotalHours;
        return $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
    }

    var totalMinutes = (int)duration.TotalMinutes;
    return $"{totalMinutes:D2}:{duration.Seconds:D2}";
}

static string? FindTool(string toolName)
{
    var exeName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
    var localPath = Path.Combine(AppContext.BaseDirectory, exeName);
    if (File.Exists(localPath))
    {
        return localPath;
    }

    return exeName;
}

static bool ToolAvailable(string? toolPath)
{
    if (string.IsNullOrWhiteSpace(toolPath))
    {
        return false;
    }

    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return false;
        }

        process.WaitForExit(2000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

static async Task<TimeSpan?> ProbeDurationAsync(string toolPath, string filePath)
{
    var psi = new ProcessStartInfo
    {
        FileName = toolPath,
        Arguments = $"-v quiet -print_format json -show_format \"{filePath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        using var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var doc = JObject.Parse(output);
        if (doc["format"]?["duration"]?.ToString() is string durationStr &&
            double.TryParse(durationStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }
    }
    catch
    {
        // Ignored; duration will be null
    }

    return null;
}

static async Task<string?> CaptureThumbnailAsync(string toolPath, string filePath, TimeSpan? duration)
{
    var seekSeconds = duration.HasValue && duration.Value.TotalSeconds > 60
        ? 60
        : 0;

    var psi = new ProcessStartInfo
    {
        FileName = toolPath,
        Arguments = $"-y -ss {seekSeconds.ToString(CultureInfo.InvariantCulture)} -i \"{filePath}\" -frames:v 1 -vf scale=-2:360 -f image2pipe -vcodec png pipe:1",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    try
    {
        using var process = Process.Start(psi);
        if (process == null)
        {
            return null;
        }

        await using var ms = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(ms);
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || ms.Length == 0)
        {
            return null;
        }

        var base64 = Convert.ToBase64String(ms.ToArray());
        return $"data:image/png;base64,{base64}";
    }
    catch
    {
        return null;
    }
}

record Episode
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Src { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string Thumbnail { get; init; } = string.Empty;
}
