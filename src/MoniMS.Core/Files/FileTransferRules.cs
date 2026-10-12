namespace MoniMS.Core.Files;

public enum TransferAction
{
    None,
    Copy,
    Move,
}

/// <summary>끌어 놓기·붙여넣기를 복사로 할지 이동으로 할지 (파일 탐색기와 같은 규칙).</summary>
public static class FileTransferRules
{
    /// <summary>
    /// 끌어 놓기: Ctrl = 복사, Shift = 이동. 아무 키도 안 누르면 같은 드라이브는 이동, 다른 드라이브는 복사.
    /// 원래 있던 폴더에 그대로 놓으면 아무 일도 없다 (Ctrl이면 복사본을 만든다).
    /// </summary>
    public static TransferAction ForDrop(IReadOnlyList<string> sources, string targetFolder, bool ctrl, bool shift)
    {
        if (sources.Count == 0 || IntoItself(sources, targetFolder))
            return TransferAction.None;
        if (ctrl)
            return TransferAction.Copy;
        if (AllIn(sources, targetFolder))
            return TransferAction.None;
        if (shift)
            return TransferAction.Move;
        return SameDrive(sources[0], targetFolder) ? TransferAction.Move : TransferAction.Copy;
    }

    /// <summary>붙여넣기: 잘라낸 것은 이동, 복사한 것은 복사 (같은 폴더면 복사본).</summary>
    public static TransferAction ForPaste(IReadOnlyList<string> sources, string targetFolder, bool cut)
    {
        if (sources.Count == 0 || IntoItself(sources, targetFolder))
            return TransferAction.None;
        if (!cut)
            return TransferAction.Copy;
        return AllIn(sources, targetFolder) ? TransferAction.None : TransferAction.Move;
    }

    /// <summary>폴더를 자기 자신이나 자기 안의 폴더로 옮기거나 복사하려는지.</summary>
    public static bool IntoItself(IEnumerable<string> sources, string targetFolder)
    {
        var target = Normalize(targetFolder);
        return sources.Select(Normalize).Any(s =>
            string.Equals(s, target, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>모든 항목이 이미 그 폴더에 있는지.</summary>
    public static bool AllIn(IEnumerable<string> sources, string folder) =>
        sources.All(s => Path.GetDirectoryName(Normalize(s)) is { } parent && PathsEqual(parent, folder));

    public static bool PathsEqual(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>같은 폴더 안에 있는지 (또는 같은 폴더인지).</summary>
    public static bool IsInside(string path, string folder)
    {
        var p = Normalize(path);
        var f = Normalize(folder);
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f.EndsWith(Path.DirectorySeparatorChar) ? f : f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(Normalize(a)), Path.GetPathRoot(Normalize(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>전체 경로, 끝의 \ 제거 (드라이브 루트 C:\ 는 그대로).</summary>
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
