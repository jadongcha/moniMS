using System.IO.Compression;
using System.Net.Http.Headers;

namespace MoniMS.Core.Shell;

/// <summary>링크/파일에서 테마를 받아 보관함에 풀고 분석한다.</summary>
public sealed class ThemeImporter
{
    private const long MaxDownloadBytes = 300L * 1024 * 1024;
    private const long MaxExtractedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 20000;

    private readonly ThemeLibrary _library;
    private readonly HttpClient _http;

    public ThemeImporter(ThemeLibrary library, HttpClient? http = null)
    {
        _library = library;
        _http = http ?? CreateClient();
    }

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub API는 User-Agent가 없으면 거부한다
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MoniMS", "0.1"));
        return client;
    }

    public async Task<ThemePackage> ImportAsync(string input, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var source = ThemeSource.Parse(input);
        var package = _library.Create(CleanName(source.SuggestedName), input.Trim());
        var files = package.FilesDirectory;

        try
        {
            switch (source.Kind)
            {
                case ThemeSourceKind.LocalFolder:
                    progress?.Report("Copying folder...");
                    CopyDirectory(source.Location, Path.Combine(files, package.Name));
                    break;
                case ThemeSourceKind.LocalZip:
                    progress?.Report("Extracting...");
                    SafeExtract(source.Location, files);
                    break;
                case ThemeSourceKind.LocalJson:
                    File.Copy(source.Location, Path.Combine(files, Path.GetFileName(source.Location)));
                    break;
                case ThemeSourceKind.RemoteZip:
                case ThemeSourceKind.RemoteFile:
                    await DownloadAsync(source, files, progress, ct).ConfigureAwait(false);
                    break;
            }

            if (source.SubPath is { } sub)
                KeepOnlySubPath(files, sub);

            progress?.Report("Analyzing...");
            ThemeScanner.Scan(package);
            if (package.Mods.Count == 0 && package.Wallpapers.Count == 0)
                throw new InvalidDataException("No Windhawk taskbar/start menu settings or wallpapers were found in this package.");

            ThemeLibrary.Save(package);
            return package;
        }
        catch
        {
            TryDelete(package.Directory); // 실패하면 반쯤 만들어진 폴더 정리
            throw;
        }
    }

    private async Task DownloadAsync(ThemeSource source, string files, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Downloading...");
        using var response = await _http.GetAsync(source.Location, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        if (response.Content.Headers.ContentLength > MaxDownloadBytes)
            throw new InvalidDataException("The file is too large (over 300 MB).");

        var temp = Path.Combine(Path.GetTempPath(), "moniMS-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidDataException("The file is too large (over 300 MB).");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    if (total % (1024 * 1024) < read)
                        progress?.Report($"Downloading... {total / (1024 * 1024)} MB");
                }
            }

            if (IsZip(temp))
            {
                progress?.Report("Extracting...");
                SafeExtract(temp, files);
            }
            else
            {
                var name = Path.GetFileName(new Uri(source.Location).AbsolutePath);
                if (string.IsNullOrEmpty(Path.GetExtension(name)))
                    name += ".json";
                File.Copy(temp, Path.Combine(files, SafeFileName(name)));
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>압축 폭탄·경로 탈출 방지 (.NET ExtractToDirectory도 ../ 경로를 막지만 크기 제한은 직접).</summary>
    internal static void SafeExtract(string zipPath, string destination)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > MaxEntries)
            throw new InvalidDataException("The archive has too many files.");
        if (zip.Entries.Sum(e => e.Length) > MaxExtractedBytes)
            throw new InvalidDataException("The archive is too large when extracted.");

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Unsafe path in archive: {entry.FullName}");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static bool IsZip(string file)
    {
        using var fs = File.OpenRead(file);
        Span<byte> head = stackalloc byte[4];
        return fs.Read(head) == 4 && head[0] == 'P' && head[1] == 'K' && head[2] == 3 && head[3] == 4;
    }

    /// <summary>github .../tree/branch/sub 링크면 그 하위 폴더만 남긴다.</summary>
    private static void KeepOnlySubPath(string files, string sub)
    {
        var top = Directory.GetDirectories(files);
        var baseDir = top.Length == 1 ? top[0] : files;
        var keep = Path.Combine(baseDir, sub.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(keep))
            return;
        var temp = files + "_sub";
        Directory.Move(keep, temp);
        Directory.Delete(files, recursive: true);
        Directory.Move(temp, files);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
    }

    internal static string CleanName(string name)
    {
        foreach (var suffix in new[] { "-main", "-master" })
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^suffix.Length];
        }
        return string.IsNullOrWhiteSpace(name) ? "Theme" : name;
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
