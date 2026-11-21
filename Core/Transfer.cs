using System.Net;
using System.Net.Http.Headers;

namespace PersonalDownloader;

public sealed record TransferUpdate(long Bytes, long? Total, string Activity, TransferRange[]? Ranges = null);

public sealed class Transfer(QueueStore store)
{
    const int BufferSize = 65536;
    static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);
    sealed class RangeUnavailable : Exception { }

    public async Task RunAsync(Download job, Action<TransferUpdate> report, CancellationToken token)
    {
        if (File.Exists(job.Destination)) throw new IOException("A file already exists at this destination.");
        Directory.CreateDirectory(job.WorkDirectory());
        TransferRange[]? lastRanges = null;
        void Publish(TransferUpdate update)
        {
            if (update.Ranges != null) lastRanges = update.Ranges;
            report(update);
        }
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await Attempt(job, Publish, attempt, token);
                return;
            }
            catch (Exception e) when (!token.IsCancellationRequested &&
                (attempt < 6 || e is OperationCanceledException or EndOfStreamException or HttpIOException ||
                    e is HttpRequestException { StatusCode: null }) &&
                (e is HttpIOException or OperationCanceledException or EndOfStreamException ||
                 e is HttpRequestException h && (h.StatusCode == null || (int)h.StatusCode >= 500 ||
                    h.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)))
            {
                int seconds = Math.Min(30, 1 << Math.Min(attempt, 5));
                report(new(Existing(job), job.Size, $"Retrying in {seconds}s · {e.GetBaseException().Message}", lastRanges));
                await Task.Delay(TimeSpan.FromSeconds(seconds), token);
            }
        }
    }

    static long Existing(Download job) => Enumerable.Range(0, job.Parts)
        .Sum(i => File.Exists(job.Part(i)) ? new FileInfo(job.Part(i)).Length : 0L);

    static async Task<HttpResponseMessage> Send(HttpClient client, Download job, long? first, long? last,
        string? validator, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (first.HasValue) request.Headers.Range = new RangeHeaderValue(first, last);
        if (validator != null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(validator));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(IdleTimeout);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    static bool Matches(HttpResponseMessage r, long first, long last, long size)
    {
        var range = r.Content.Headers.ContentRange;
        return r.StatusCode == HttpStatusCode.PartialContent && range?.Unit == "bytes" &&
            range.From == first && range.To == last && range.Length == size &&
            (r.Content.Headers.ContentLength is not long length || length == last - first + 1) &&
            r.Content.Headers.ContentEncoding.Count == 0;
    }

    void Plan(Download job, string? tag, string? resource, long? size, int parts)
    {
        if (tag != null && job.ETag == tag && job.Resource == resource && job.Size == size && job.Parts > 0) return;
        job.Generation = Guid.NewGuid();
        job.ETag = tag;
        job.Resource = resource;
        job.Size = size;
        job.Parts = parts;
        store.Save(job);
        foreach (string path in Directory.GetFiles(job.WorkDirectory(), "*.part")) File.Delete(path);
    }

    async Task Attempt(Download job, Action<TransferUpdate> report, int attempt, CancellationToken token)
    {
        var available = Connections.List().ToDictionary(a => a.Id, a => a.Name);
        var ids = job.Interfaces.Where(available.ContainsKey).ToArray();
        string? Route(int i) => ids.Length == 0 ? null : ids[(i + attempt) % ids.Length];
        string RouteName(int i) => Route(i) is string id ? available[id] : "Windows route";
        string primaryRoute = RouteName(0);
        using var client = Connections.Create(Route(0), name => primaryRoute = name);
        using var probe = await Send(client, job, 0, 0, null, token);
        if (probe.StatusCode == HttpStatusCode.OK)
        {
            await Whole(job, probe, primaryRoute, report, token);
            return;
        }
        if (probe.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable) probe.EnsureSuccessStatusCode();
        long? size = probe.Content.Headers.ContentRange?.Length;
        var tag = probe.Headers.ETag;
        if (size > 0 && tag is { IsWeak: false } && Matches(probe, 0, 0, size.Value))
        {
            int parts = (int)Math.Min(4, 1 + (size.Value - 1) / (4 * 1024 * 1024));
            Plan(job, tag.ToString(), probe.RequestMessage!.RequestUri!.AbsoluteUri, size, parts);
            var ranges = TransferRange.Restore(job, i => RouteName(attempt == 0 ? i : 0));
            long received = ranges.Sum(r => r.Saved);
            report(new(received, size, "Downloading", ranges));
            object progressGate = new();
            using var group = CancellationTokenSource.CreateLinkedTokenSource(token);
            bool useWhole = false;
            async Task Segment(int i)
            {
                try
                {
                    long width = size.Value / job.Parts;
                    long start = width * i;
                    long end = i == job.Parts - 1 ? size.Value - 1 : start + width - 1;
                    using var output = new FileStream(job.Part(i), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
                    if (output.Length > end - start + 1) throw new InvalidDataException("Partial file exceeds its assigned range.");
                    start += output.Length;
                    if (start > end) return;
                    output.Position = output.Length;
                    // On retry, give surviving connections a turn at all unfinished segments.
                    using var connection = Connections.Create(Route(attempt == 0 ? i : 0), name => ranges[i].Route = name);
                    using var r = await Send(connection, job, start, end, job.ETag, group.Token);
                    if (r.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable) r.EnsureSuccessStatusCode();
                    if (!Matches(r, start, end, size.Value) ||
                        r.RequestMessage!.RequestUri!.AbsoluteUri != job.Resource ||
                        (r.Headers.ETag != null && r.Headers.ETag.ToString() != job.ETag)) throw new RangeUnavailable();
                    await Copy(r, output, end - start + 1,
                        n =>
                        {
                            lock (progressGate)
                            {
                                ranges[i].Write(n);
                                report(new(received += n, size, "Downloading", ranges));
                            }
                        }, group.Token);
                }
                catch (RangeUnavailable) { useWhole = true; group.Cancel(); throw; }
            }
            try { await Task.WhenAll(Enumerable.Range(0, job.Parts).Select(Segment)); }
            catch when (useWhole && !token.IsCancellationRequested) { }
            if (!useWhole)
            {
                report(new(size.Value, size, "Finishing", ranges));
                string combined = Path.Combine(job.WorkDirectory(), "assembled.tmp");
                string ready = job.Part(0);
                if (job.Parts > 1)
                {
                    using var output = File.Create(combined);
                    byte[] buffer = new byte[BufferSize];
                    for (int i = 0; i < job.Parts; i++)
                    {
                        report(new(size.Value, size, $"Assembling range {(char)('A' + i)}", ranges));
                        using var input = File.OpenRead(job.Part(i));
                        int count;
                        while ((count = await input.ReadAsync(buffer, token)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, count), token);
                            ranges[i].Merge(count);
                        }
                    }
                    output.Flush(true);
                    ready = combined;
                }
                Finish(job, ready);
                return;
            }
        }
        using var full = await Send(client, job, null, null, null, token);
        await Whole(job, full, primaryRoute, report, token);
    }

    async Task Whole(Download job, HttpResponseMessage response, string route, Action<TransferUpdate> report, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Expected a complete HTTP 200 response.");
        if (response.Content.Headers.ContentEncoding.Any(v => !v.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The server returned an encoded body despite the identity encoding request.");
        Plan(job, null, null, response.Content.Headers.ContentLength, 1);
        const string activity = "Downloading · restart required if interrupted";
        TransferRange[] ranges = [new(0, job.Size, 0, route)];
        report(new(0, job.Size, activity, ranges));
        using (var output = File.Create(job.Part(0)))
            await Copy(response, output, job.Size, n =>
            {
                ranges[0].Write(n);
                report(new(ranges[0].Saved, job.Size, activity, ranges));
            }, token);
        Finish(job, job.Part(0));
    }

    static async Task Copy(HttpResponseMessage response, FileStream output, long? expected, Action<int> advance, CancellationToken token)
    {
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        byte[] buffer = new byte[BufferSize];
        long written = 0;
        while (true)
        {
            timeout.CancelAfter(IdleTimeout);
            int count;
            try { count = await input.ReadAsync(buffer, timeout.Token); }
            catch (IOException e) { throw new HttpRequestException("Transfer interrupted.", e); }
            timeout.CancelAfter(Timeout.Infinite);
            if (count == 0) break;
            written += count;
            if (written > expected) throw new InvalidDataException("Server sent more bytes than requested.");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            advance(count);
        }
        if (expected.HasValue && written != expected) throw new EndOfStreamException("Connection closed before the transfer finished.");
        output.Flush(true);
    }

    void Finish(Download job, string ready)
    {
        job.State = "Finalizing";
        job.ReadyFile = Path.GetFileName(ready);
        job.Size = new FileInfo(ready).Length;
        store.Save(job);
        File.Move(ready, job.Destination);
        job.State = "Completed";
        job.Detail = "";
        store.Save(job);
        try { Directory.Delete(job.WorkDirectory(), true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public static void RecoverFinalization(Download job, QueueStore store)
    {
        string? ready = job.ReadyFile;
        if (ready == null || Path.GetFileName(ready) != ready) { job.State = "Failed"; return; }
        string path = Path.Combine(job.WorkDirectory(), ready);
        if (File.Exists(path) && !File.Exists(job.Destination)) File.Move(path, job.Destination);
        if (!File.Exists(path) && File.Exists(job.Destination) && new FileInfo(job.Destination).Length == job.Size)
        {
            job.State = "Completed";
            store.Save(job);
            if (Directory.Exists(job.WorkDirectory())) Directory.Delete(job.WorkDirectory(), true);
        }
        else { job.State = "Failed"; job.Detail = "Check the destination before retrying."; store.Save(job); }
    }
}
