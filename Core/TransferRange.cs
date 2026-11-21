namespace PersonalDownloader;

// One worker writes each range. The dashboard samples its counters on the UI thread.
public sealed class TransferRange(long start, long? length, long existing, string route)
{
    long saved = existing;
    long assembled;
    public long Start { get; } = start;
    public long? Length { get; } = length;
    public string Route { get; internal set; } = route;
    public long Existing { get; } = existing;
    public long Saved => Interlocked.Read(ref saved);
    public long Assembled => Interlocked.Read(ref assembled);
    internal void Write(int count) => Interlocked.Add(ref saved, count);
    internal void Merge(int count) => Interlocked.Add(ref assembled, count);

    public static TransferRange[] Restore(Download job, Func<int, string>? route = null)
    {
        long width = job.Size.GetValueOrDefault() / Math.Max(1, job.Parts);
        return Enumerable.Range(0, job.Parts).Select(i =>
        {
            long? length = i == job.Parts - 1 ? job.Size - width * i : width;
            long bytes = job.State == "Completed" ? length.GetValueOrDefault() :
                File.Exists(job.Part(i)) ? new FileInfo(job.Part(i)).Length : 0;
            return new TransferRange(i * width, length, bytes, bytes > 0 && bytes == length ? "On disk" : route?.Invoke(i) ?? "On disk");
        }).ToArray();
    }
}
