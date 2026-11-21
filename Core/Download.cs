using System.Text.Json;

namespace PersonalDownloader;

public sealed class Download
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Url { get; init; }
    public required string Destination { get; init; }
    public string[] Interfaces { get; set; } = [];
    public string State { get; set; } = "Queued";
    public string Detail { get; set; } = "";
    public string? ETag { get; set; }
    public string? Resource { get; set; }
    public long? Size { get; set; }
    public int Parts { get; set; }
    public string? ReadyFile { get; set; }
    public Guid Generation { get; set; } = Guid.NewGuid();
    public string WorkDirectory() => Path.Combine(Path.GetDirectoryName(Destination)!, $".pd-{Id:N}");
    public string Part(int index) => Path.Combine(WorkDirectory(), $"{Generation:N}-{index}.part");

    public static Uri ParseUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length != 0)
            throw new ArgumentException("Enter an HTTP or HTTPS download link.");
        return uri;
    }
}

public sealed class QueueStore(string directory)
{
    public void Remove(Download job)
    {
        if (Directory.Exists(job.WorkDirectory())) Directory.Delete(job.WorkDirectory(), true);
        File.Delete(Path.Combine(directory, job.Id + ".json"));
    }
    public void Save(Download job)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, job.Id + ".json");
        using (var file = File.Create(path + ".tmp"))
        {
            JsonSerializer.Serialize(file, job);
            file.Flush(true);
        }
        File.Move(path + ".tmp", path, true);
    }

    public IEnumerable<Download> Load(Action<string> warn)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(File.GetCreationTimeUtc))
        {
            Download? job;
            try
            {
                job = JsonSerializer.Deserialize<Download>(File.ReadAllText(path));
                if (job == null || !Path.IsPathFullyQualified(job.Destination) ||
                    job.Parts is < 0 or > 4 || job.Interfaces == null)
                    throw new InvalidDataException("Invalid queue entry.");
                Download.ParseUrl(job.Url);
            }
            catch (Exception e) { warn($"Could not load {Path.GetFileName(path)}. {e.Message}"); continue; }
            yield return job;
        }
    }
}
