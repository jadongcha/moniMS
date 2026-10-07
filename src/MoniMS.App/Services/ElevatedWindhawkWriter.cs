using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MoniMS.Core.Shell;

namespace MoniMS.App.Services;

/// <summary>
/// Windhawk 설정 쓰기. 설치형 Windhawk는 HKLM에 저장하므로 일반 권한으론 실패한다.
/// 그때만 MoniMS.exe를 "--windhawk-write" 모드로 관리자 권한 실행(UAC 1회)해서 대신 쓰게 한다.
/// </summary>
public sealed class ElevatedWindhawkWriter(IWindhawkStorage storage, ILogger<ElevatedWindhawkWriter> logger) : IWindhawkSettingsWriter
{
    public const string HelperArgument = "--windhawk-write";
    private const int ErrorCancelled = 1223; // 사용자가 UAC에서 "아니요"

    public void Write(IReadOnlyList<ModSettingsWrite> items)
    {
        if (items.Count == 0)
            return;
        try
        {
            foreach (var item in items)
                storage.WriteSettings(item.ModId, item.Settings);
        }
        catch (UnauthorizedAccessException)
        {
            logger.LogInformation("Windhawk settings need administrator rights; launching elevated helper");
            RunElevated(items);
        }
    }

    private void RunElevated(IReadOnlyList<ModSettingsWrite> items)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MoniMS");
        Directory.CreateDirectory(dir);
        var id = Guid.NewGuid().ToString("N");
        var request = Path.Combine(dir, $"wh-request-{id}.json");
        var result = Path.Combine(dir, $"wh-result-{id}.txt");
        File.WriteAllText(request, ThemeApplier.SerializeWrites(items));

        try
        {
            var args = $"{HelperArgument} \"{request}\" \"{result}\"";
            if (!string.IsNullOrWhiteSpace(storage.CustomAppRoot))
                args += $" \"{storage.CustomAppRoot}\"";

            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args)
            {
                UseShellExecute = true,
                Verb = "runas",
            }) ?? throw new InvalidOperationException("Could not start the elevated helper.");

            if (!process.WaitForExit(TimeSpan.FromMinutes(2)))
                throw new TimeoutException("The elevated helper did not finish in time.");

            var message = File.Exists(result) ? File.ReadAllText(result) : "No result from the elevated helper.";
            if (process.ExitCode != 0 || message != "OK")
                throw new InvalidOperationException(message);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("Administrator permission was declined, so nothing was changed.", ex);
        }
        finally
        {
            TryDelete(request);
            TryDelete(result);
        }
    }

    /// <summary>관리자 권한으로 실행된 헬퍼 프로세스의 본체. 앱 UI 없이 쓰고 끝낸다.</summary>
    public static int RunHelper(string[] args)
    {
        // args: --windhawk-write <request> <result> [customRoot]
        var resultPath = args.Length > 2 ? args[2] : null;
        try
        {
            var writes = ThemeApplier.DeserializeWrites(File.ReadAllText(args[1]));
            var storage = new WindhawkStorage { CustomAppRoot = args.Length > 3 ? args[3] : null };
            foreach (var w in writes)
                storage.WriteSettings(w.ModId, w.Settings);
            if (resultPath is not null)
                File.WriteAllText(resultPath, "OK");
            return 0;
        }
        catch (Exception ex)
        {
            if (resultPath is not null)
            {
                try
                {
                    File.WriteAllText(resultPath, ex.Message);
                }
                catch (IOException)
                {
                }
            }
            return 1;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
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
