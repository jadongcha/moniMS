using System.Runtime.InteropServices;
using MoniMS.Core.Interop;

namespace MoniMS.Core.Media;

/// <summary>음량 (0 ~ 1)과 음소거.</summary>
public readonly record struct VolumeState(float Level, bool Muted);

/// <summary>
/// 앱별 음량(볼륨 믹서의 앱 항목)과 Windows 전체 음량. 기본 출력 장치(스피커/헤드폰) 기준.
/// 앱은 프로세스 이름으로 찾는다. 브라우저는 소리를 내는 프로세스가 따로 있어도 이름이 같다 (chrome.exe 등).
/// </summary>
public sealed class AudioVolume
{
    private const int eRender = 0;
    private const int eMultimedia = 1;
    private const uint CLSCTX_ALL = 0x17;
    private const int AudioSessionStateActive = 1;
    private const int AudioSessionStateExpired = 2;

    private static readonly Guid SessionManagerIid = typeof(IAudioSessionManager2).GUID;
    private static readonly Guid EndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;

    /// <summary>우리가 바꾼 음량이라는 표시 (Windows가 알림에 함께 실어 보낸다).</summary>
    private static Guid _eventContext = Guid.NewGuid();

    /// <summary>프로세스 ID → 이름. 소리 세션을 찾을 때마다 프로세스를 열지 않도록. 세션이 사라진 프로세스는 지운다 (ID 재사용).</summary>
    private readonly Dictionary<uint, string?> _processNames = [];

    // ================= Windows 전체 =================

    public static VolumeState? GetSystem() => WithEndpoint<VolumeState?>(v =>
        v.GetMasterVolumeLevelScalar(out var level) == 0 && v.GetMute(out var muted) == 0 ? new VolumeState(level, muted) : null);

    public static bool SetSystem(float level) => WithEndpoint(v =>
    {
        var ok = v.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 1), ref _eventContext) == 0;
        if (ok && level > 0)
            v.SetMute(false, ref _eventContext); // 막대를 올리면 음소거도 풀린다 (Windows 음량 플라이아웃과 같게)
        return ok;
    });

    public static bool SetSystemMute(bool mute) => WithEndpoint(v => v.SetMute(mute, ref _eventContext) == 0);

    // ================= 앱별 =================

    /// <param name="processNames">확장자 없는 프로세스 이름 (대소문자 무시).</param>
    /// <returns>그 앱의 소리 세션이 없으면(아직 소리를 낸 적 없음) null.</returns>
    public VolumeState? GetApp(IReadOnlyCollection<string> processNames)
    {
        VolumeState? result = null;
        var foundActive = false;
        ForEachAppSession(processNames, (volume, state) =>
        {
            // 재생 중인 세션의 값을 우선한다
            if (foundActive || volume.GetMasterVolume(out var level) != 0 || volume.GetMute(out var muted) != 0)
                return;
            result = new VolumeState(level, muted);
            foundActive = state == AudioSessionStateActive;
        });
        return result;
    }

    /// <summary>그 앱의 소리 세션 모두에 적용. 세션이 하나도 없으면 false.</summary>
    public bool SetApp(IReadOnlyCollection<string> processNames, float level)
    {
        level = Math.Clamp(level, 0, 1);
        var any = false;
        ForEachAppSession(processNames, (volume, _) =>
        {
            any |= volume.SetMasterVolume(level, ref _eventContext) == 0;
            if (level > 0)
                volume.SetMute(false, ref _eventContext);
        });
        return any;
    }

    public bool SetAppMute(IReadOnlyCollection<string> processNames, bool mute)
    {
        var any = false;
        ForEachAppSession(processNames, (volume, _) => any |= volume.SetMute(mute, ref _eventContext) == 0);
        return any;
    }

    // ================= Core Audio =================

    private static T WithEndpoint<T>(Func<IAudioEndpointVolume, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
        IMMDevice? device = null;
        object? instance = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0)
                return default!;
            var iid = EndpointVolumeIid;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out instance) != 0)
                return default!;
            return action((IAudioEndpointVolume)instance);
        }
        catch (COMException)
        {
            return default!; // 출력 장치가 없음 등
        }
        finally
        {
            Release(instance);
            Release(device);
            Release(enumerator);
        }
    }

    private void ForEachAppSession(IReadOnlyCollection<string> processNames, Action<ISimpleAudioVolume, int> action)
    {
        if (processNames.Count == 0)
            return;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
        IMMDevice? device = null;
        object? instance = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device) != 0)
                return;
            var iid = SessionManagerIid;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out instance) != 0)
                return;
            if (((IAudioSessionManager2)instance).GetSessionEnumerator(out sessions) != 0 || sessions.GetCount(out var count) != 0)
                return;
            var seen = new HashSet<uint>();
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0)
                    continue;
                try
                {
                    // GetProcessId는 여러 프로세스가 함께 쓰는 세션(미디어 재생 등)이면 0이 아닌 성공 코드를 돌려준다
                    if (session.GetState(out var state) != 0 || state == AudioSessionStateExpired
                        || session.GetProcessId(out var pid) < 0 || pid == 0) // pid 0 = 시스템 소리
                        continue;
                    seen.Add(pid);
                    var name = ProcessName(pid);
                    if (name is not null && processNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        action((ISimpleAudioVolume)session, state);
                }
                finally
                {
                    Release(session);
                }
            }
            foreach (var pid in _processNames.Keys.Where(p => !seen.Contains(p)).ToList())
                _processNames.Remove(pid);
        }
        catch (COMException)
        {
            // 출력 장치가 없음 등
        }
        finally
        {
            Release(sessions);
            Release(instance);
            Release(device);
            Release(enumerator);
        }
    }

    private unsafe string? ProcessName(uint pid)
    {
        if (_processNames.TryGetValue(pid, out var cached))
            return cached;

        string? name = null;
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = stackalloc char[1024];
                uint size = 1024;
                if (NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                    name = Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size));
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
        _processNames[pid] = name;
        return name;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }
}
