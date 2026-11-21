using System.Net.Http.Headers;

namespace PersonalDownloader;

public static class DownloadName
{
    public static async Task<string> ResolveAsync(HttpClient client, Uri url, CancellationToken token)
    {
        string fallback = FromUrl(url) ?? "download";
        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            using var request = new HttpRequestMessage(method, url);
            if (method == HttpMethod.Get) request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) continue;
            var disposition = response.Content.Headers.ContentDisposition;
            var name = Clean(disposition?.FileNameStar) ?? Clean(disposition?.FileName);
            if (name != null) return name;
            fallback = FromUrl(response.RequestMessage?.RequestUri ?? url) ?? fallback;
            if (method == HttpMethod.Get || Path.HasExtension(fallback) && Path.GetExtension(fallback).Any(char.IsLetter))
                return fallback;
        }
        return fallback;
    }

    public static string? FromUrl(Uri url) => Clean(Uri.UnescapeDataString(url.Segments.LastOrDefault() ?? ""));

    static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string name = Path.GetFileName(raw.Trim().Trim('"')).Trim().TrimEnd('.');
        if (name.Length == 0 || name.Length > 240 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])) return null;
        return name;
    }
}
