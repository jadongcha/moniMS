using System.Security.Cryptography;

namespace MoniMS.Core.Presets;

/// <summary>
/// 사진 위젯이 보여주는 이미지 보관 (%APPDATA%\MoniMS\images).
/// 고른 파일을 복사해 두므로 원본을 지우거나 옮겨도, 프리셋을 지워도 위젯은 그대로 보인다.
/// 같은 내용은 한 번만 저장한다 (파일 이름 = 내용 해시).
/// </summary>
public sealed class ImageStore
{
    public static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".jfif"];

    public ImageStore(string? root = null)
    {
        RootDirectory = root ?? Path.Combine(AppPaths.DataDirectory, "images");
    }

    public string RootDirectory { get; }

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>이미 보관함 안의 파일인지.</summary>
    public bool Contains(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(RootDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>파일을 보관함에 복사하고 그 경로를 반환 (이미 보관함 안이면 그대로).</summary>
    public string Import(string source)
    {
        if (Contains(source))
            return Path.GetFullPath(source);
        if (!File.Exists(source))
            throw new FileNotFoundException("Image not found.", source);

        string hash;
        using (var stream = File.OpenRead(source))
            hash = Convert.ToHexStringLower(SHA256.HashData(stream))[..16];
        Directory.CreateDirectory(RootDirectory);
        var target = Path.Combine(RootDirectory, $"img-{hash}{Path.GetExtension(source).ToLowerInvariant()}");
        if (!File.Exists(target))
        {
            var temp = target + ".tmp";
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true); // 복사 중에 끊겨도 반쯤 쓴 파일이 남지 않게
        }
        return target;
    }

    /// <summary>지금 쓰는 이미지(<paramref name="keep"/>)만 남기고 정리한다.</summary>
    public void RemoveUnused(string? keep)
    {
        if (!Directory.Exists(RootDirectory))
            return;
        var keepFull = keep is null ? null : Path.GetFullPath(keep);
        foreach (var file in Directory.GetFiles(RootDirectory))
        {
            if (string.Equals(Path.GetFullPath(file), keepFull, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 아직 읽는 중이면 다음 기회에
            }
        }
    }
}
