using System.Buffers;
using System.IO.Enumeration;
using MoniMS.Core.Interop;

namespace MoniMS.Core.Files;

public enum FileSortColumn
{
    Name,
    Extension,
}

/// <summary>폴더 안의 파일이나 폴더 하나. 이름은 "이름"과 "확장자" 열로 나눠서 보여 준다.</summary>
public sealed class FileEntry
{
    public FileEntry(string fullPath, bool isFolder, FileAttributes attributes)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        IsFolder = isFolder;
        Attributes = attributes;
        (BaseName, Extension) = FolderReader.SplitName(Name, isFolder);
    }

    public string FullPath { get; }

    /// <summary>확장자까지 포함한 실제 이름.</summary>
    public string Name { get; }

    /// <summary>이름 열에 보이는 부분 (확장자 제외).</summary>
    public string BaseName { get; }

    /// <summary>확장자 열에 보이는 부분 (점 없이 소문자). 폴더나 확장자 없는 파일은 빈 문자열.</summary>
    public string Extension { get; }

    public bool IsFolder { get; }
    public FileAttributes Attributes { get; }
    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
}

/// <summary>파일 위젯이 보여 줄 폴더 내용 읽기 · 정렬 · 이름 규칙 (파일 탐색기와 같은 규칙).</summary>
public static class FolderReader
{
    private static readonly SearchValues<char> InvalidNameChars = SearchValues.Create("\\/:*?\"<>|");

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 폴더 내용 (정렬 안 됨). 숨김 파일은 파일 탐색기 설정을 따른다:
    /// showHidden = "숨김 항목 표시", showProtected = "보호된 운영 체제 파일 숨기기"를 끈 상태.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">폴더를 열 권한이 없음</exception>
    /// <exception cref="DirectoryNotFoundException">폴더가 없음</exception>
    public static List<FileEntry> List(string folder, bool showHidden, bool showProtected)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = 0, // 기본값은 숨김·시스템 항목을 건너뛴다. 여기서는 아래에서 직접 거른다.
            IgnoreInaccessible = false,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };
        var entries = new FileSystemEnumerable<FileEntry>(folder,
            (ref FileSystemEntry e) => new FileEntry(e.ToFullPath(), e.IsDirectory, e.Attributes), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => IsShown(e.Attributes, showHidden, showProtected),
        };
        return [.. entries];
    }

    /// <summary>
    /// 폴더가 있는지 바로 알 수 있으면 확인하고, 네트워크 폴더는 있다고 본다.
    /// 연결이 끊긴 네트워크 공유는 확인에 수십 초가 걸려서 UI 스레드에서 물으면 앱이 멈춘다.
    /// </summary>
    public static bool ProbablyExists(string folder) => IsNetworkPath(folder) || Directory.Exists(folder);

    /// <summary>\\서버\공유 경로이거나 네트워크 드라이브(Z: 등)에 있는지. 네트워크에 접속하지 않고 판단한다.</summary>
    public static bool IsNetworkPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>숨김 항목은 "숨김 항목 표시"를 켰을 때만, 숨김+시스템(보호된 OS 파일)은 그것까지 켰을 때만.</summary>
    public static bool IsShown(FileAttributes attributes, bool showHidden, bool showProtected)
    {
        if ((attributes & FileAttributes.Hidden) == 0)
            return true;
        if (!showHidden)
            return false;
        return (attributes & FileAttributes.System) == 0 || showProtected;
    }

    /// <summary>
    /// "report.final.pdf" → ("report.final", "pdf"). 폴더, 확장자 없는 파일, 점으로 시작하는 파일(.gitignore)은
    /// 이름 전체가 이름이다.
    /// </summary>
    public static (string BaseName, string Extension) SplitName(string name, bool isFolder)
    {
        var dot = name.LastIndexOf('.');
        if (isFolder || dot <= 0 || dot == name.Length - 1)
            return (name, "");
        return (name[..dot], name[(dot + 1)..].ToLowerInvariant());
    }

    /// <summary>폴더가 항상 위. 이름은 파일 탐색기처럼 숫자를 크기대로 비교한다 (file2 &lt; file10).</summary>
    public static IComparer<FileEntry> GetComparer(FileSortColumn by, bool descending) =>
        Comparer<FileEntry>.Create((a, b) =>
        {
            if (a.IsFolder != b.IsFolder)
                return a.IsFolder ? -1 : 1;
            var r = by == FileSortColumn.Extension ? CompareNames(a.Extension, b.Extension) : 0;
            if (r == 0)
                r = CompareNames(a.Name, b.Name);
            return descending ? -r : r;
        });

    public static List<FileEntry> Sort(IEnumerable<FileEntry> entries, FileSortColumn by, bool descending)
    {
        var list = entries.ToList();
        list.Sort(GetComparer(by, descending));
        return list;
    }

    /// <summary>파일 탐색기 순서 (StrCmpLogicalW). 같다고 보는 이름(file01, file1)은 글자 순으로 갈라 순서를 항상 정한다.</summary>
    public static int CompareNames(string a, string b)
    {
        var r = NativeMethods.StrCmpLogicalW(a, b);
        return r != 0 ? r : string.CompareOrdinal(a, b);
    }

    /// <summary>"New folder", 있으면 "New folder (2)", "New folder (3)" … (파일 탐색기와 같은 방식).</summary>
    public static string UniqueName(string folder, string baseName)
    {
        var name = baseName;
        for (var i = 2; Path.Exists(Path.Combine(folder, name)); i++)
            name = $"{baseName} ({i})";
        return name;
    }

    /// <summary>
    /// 이름 바꾸기 입력 정리: 앞뒤 공백과 끝의 점은 Windows가 어차피 떼어 내므로 미리 뗀다.
    /// 쓸 수 없는 이름이면 이유를 돌려준다 (정상이면 null).
    /// </summary>
    public static string? ValidateName(string input, out string name)
    {
        name = input.Trim().TrimEnd('.', ' ');
        if (name.Length == 0)
            return "You must type a file name.";
        if (name.AsSpan().ContainsAny(InvalidNameChars) || name.Any(char.IsControl))
            return "A file name can't contain any of the following characters: \\ / : * ? \" < > |";
        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem))
            return $"'{name}' is a name reserved by Windows.";
        if (name.Length > 255)
            return "The file name is too long.";
        return null;
    }
}
