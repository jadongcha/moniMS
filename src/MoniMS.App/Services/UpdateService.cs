using System.Reflection;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace MoniMS.App.Services;

/// <summary>
/// GitHub Releases를 통한 자동 업데이트 (Velopack).
/// 설치 프로그램으로 설치된 경우에만 동작하고, 개발 중 실행(dotnet run 등)에서는 꺼져 있다.
/// </summary>
public sealed class UpdateService
{
    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager? _manager;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        RepositoryUrl = typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;

        // 저장소 주소가 아직 기본값(OWNER)이면 업데이트 확인 안 함
        if (RepositoryUrl is { Length: > 0 } && !RepositoryUrl.Contains("/OWNER/", StringComparison.Ordinal))
        {
            try
            {
                _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update manager could not be created");
            }
        }
    }

    public string? RepositoryUrl { get; }

    /// <summary>설치 프로그램으로 설치돼서 업데이트가 가능한지.</summary>
    public bool IsSupported => _manager?.IsInstalled == true;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";

    /// <summary>새 버전이 있으면 반환, 없으면 null.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        if (!IsSupported)
            return null;
        return await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
    }

    public Task DownloadAsync(UpdateInfo update, Action<int>? progress = null) =>
        _manager!.DownloadUpdatesAsync(update, progress);

    /// <summary>앱을 즉시 종료하고 업데이트를 적용한 뒤 다시 실행한다.</summary>
    public void ApplyAndRestart(UpdateInfo update) =>
        _manager!.ApplyUpdatesAndRestart(update.TargetFullRelease);
}
