# MoniMS 개발 문서

사용자용 안내는 [README.md](../README.md) / [README.ko.md](../README.ko.md)를 보세요. 이 문서는 코드를 고치거나 릴리스하는 사람을 위한 문서입니다.

## 기능 요약

- **프리셋**: 배경화면(모니터별), 테마색, 바탕화면 아이콘 배치, 위젯 4종(정보·사진·파일·음악)의 상태를 한 번에 저장하고 전환합니다. 적용하기 전에 미리보기로 확인할 수 있습니다.
- **정보 위젯**: 바탕화면에 붙어 있는 시스템 정보 위젯입니다. CPU·메모리·GPU 사용량과 그래프, 디스크, IP·네트워크 속도, Windows 버전, 하드웨어 정보를 보여 줍니다.
- **사진 위젯**: 사진이나 움직이는 GIF를 바탕화면에 띄웁니다. 정보 위젯처럼 항상 클릭이 통과하고, 편집 모드에서만 옮기거나 크기를 바꿀 수 있습니다 (사진 비율 유지).
- **파일 위젯**: 바탕화면에 붙어 있는 작은 파일 탐색기입니다 ("자세히" 보기, 이름·확장자 열). 다른 위젯과 달리 클릭과 키보드 입력을 받습니다.
- **음악 위젯**: Windows 미디어 제어에 나오는 앱(Spotify, 브라우저의 YouTube·YouTube Music 등)의 지금 곡을 보여 주고 이전·재생/일시 정지·다음·음량을 조절합니다. 클릭은 받지만 키보드는 받지 않습니다. 처음에는 숨겨져 있습니다.
- UI는 영어, 글꼴은 **JetBrains Mono**를 앱에 내장했습니다 (SIL OFL 1.1). 정보 위젯 글꼴은 설정에서 바꿀 수 있고, 프리셋에도 함께 저장됩니다.

## 실행 방법

필요한 것: Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json`이 10.0.100 이상을 요구), [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) (아무 버전, Visual Studio의 "Windows 11 SDK" 구성 요소로도 설치됨), 그리고 Visual Studio 2026 / 2022 최신판 또는 VS Code + C# Dev Kit.

Windows SDK는 빌드할 때만 씁니다. 음악 위젯이 쓰는 WinRT API(`Windows.Media.Control`)의 C# 코드를 C#/WinRT가 SDK의 형식 정보(`Windows Kits\10\UnionMetadata`)로 만들어 MoniMS.Core에 넣습니다. Windows SDK 전체 투영(`Microsoft.Windows.SDK.NET.dll`, 약 25MB) 대신 필요한 형식만 만들어서 배포 크기는 약 2MB만 늘어납니다 (`MoniMS.Core.csproj`의 `CsWinRTIncludes`). 투영할 형식을 더하다가 빌드 오류가 나면 그 형식이 끌어오는 다른 네임스페이스를 `CsWinRTExcludes`로 빼거나 필요한 형식을 더하세요.

```powershell
cd moniMS
dotnet run --project src/MoniMS.App      # 실행
dotnet test                              # 단위 테스트
dotnet publish src/MoniMS.App -c Release -r win-x64 --self-contained false -o publish   # 배포용 빌드 (릴리스는 아래 "릴리스 방법" 참고)
```

Visual Studio에서는 `MoniMS.sln`을 열고 **MoniMS.App**을 시작 프로젝트로 지정한 뒤 F5를 누르면 됩니다.

실행하면 알림 영역(트레이)에 아이콘이 생깁니다. 아이콘을 더블클릭하면 설정 창이 열리고, 우클릭 메뉴에서 할 수 있는 일:

- 프리셋 적용 (마지막으로 적용한 프리셋에 체크 표시)
- 현재 상태를 프리셋으로 바로 저장
- 위젯 4종을 각각 표시/숨기기 (사진·파일 위젯은 설정 창에서 사진이나 폴더를 고른 뒤부터)
- **Edit widgets (move & resize)**: 위젯을 모두 편집 모드로 바꿉니다(음악 위젯은 켜 둔 경우만). 정보·사진 위젯은 평소에 클릭이 통과하고 편집 모드에서만 잡을 수 있습니다. 사진·파일·음악 위젯은 가장자리를 끌어 크기도 바꿀 수 있습니다.
- 설정 창 열기, 종료

설정 창은 **Presets**, **Info widget**, **Image widget**, **File widget**, **Media widget**, **General** 탭으로 되어 있고, 오른쪽 위 스위치로 라이트/다크를 바꿉니다.

`--background` 인자를 붙이면 설정 창 없이 트레이로만 시작합니다. Windows 시작 시 자동 실행(`HKCU\...\CurrentVersion\Run`의 `MoniMS` 값)이 이 방식을 씁니다.

한 번에 하나만 실행됩니다. 이미 실행 중일 때 다시 실행하면 실행 중인 쪽의 설정 창이 뜹니다. 단, 실행 중인 MoniMS가 지금 실행한 exe 파일보다 먼저 시작됐으면(업데이트하거나 다시 빌드한 직후) 예전 빌드로 보고 종료시킨 뒤 새 빌드가 대신 실행됩니다. 그래서 설치된 MoniMS가 켜져 있을 때 새로 빌드해 실행하면 설치된 쪽이 꺼집니다.

## 프로젝트 구조

```
MoniMS.sln
├─ src/MoniMS.Core            UI와 무관한 로직 (테스트 가능)
│  ├─ AppPaths.cs             데이터 폴더 위치 (%APPDATA%\MoniMS)
│  ├─ SystemInfo/             CPU·GPU·메모리·네트워크·디스크·OS·하드웨어 정보 수집
│  ├─ Presets/                프리셋 모델, JSON 저장소, PresetService (캡처/적용),
│  │                          위젯 4종 레이아웃 모델, WidgetSizing(화면 비율), ImageStore(사진 보관)
│  ├─ Desktop/                배경화면(COM), 테마(레지스트리), 아이콘 배치(ListView), 색상 유틸
│  ├─ Settings/               앱 설정, 자동 실행 등록
│  ├─ Files/                  파일 위젯: 폴더 읽기·정렬·이름 규칙, 끌어 놓기/붙여넣기 규칙
│  ├─ Media/                  음악 위젯: 미디어 세션 감시·조작(MediaSessions), 보여 줄 세션 고르기, 재생 위치 추정,
│  │                          앱 ID → 앱 이름·프로세스(MediaApps), 앱별·전체 음량(AudioVolume, Core Audio)
│  └─ Interop/                Win32 P/Invoke, COM 인터페이스 (배경화면, Core Audio)
├─ src/MoniMS.App             WPF 앱 (MVVM: CommunityToolkit.Mvvm, DI: Microsoft.Extensions.DependencyInjection)
│  ├─ Program.cs              시작점: Velopack 처리 → WPF 앱 실행
│  ├─ App.xaml.cs             중복 실행 방지, DI 구성, 위젯·트레이 시작, 업데이트 확인
│  ├─ Views/                  WidgetWindow(정보 위젯), ImageWidgetWindow(사진 위젯), FileWidgetWindow(파일 위젯),
│  │                          MediaWidgetWindow(음악 위젯),
│  │                          ManagerWindow(설정), InputDialog
│  ├─ ViewModels/             위젯 ViewModel(정보 위젯은 구역별), 설정 탭별 ViewModel, 프리셋 미리보기,
│  │                          ColorPickerViewModel (색 표 고르기, 설정 창의 DataTemplate과 함께 정보·음악 위젯이 같이 씀)
│  ├─ Services/               WidgetController·ImageWidgetController·FileWidgetController·MediaWidgetController (위젯 창 관리),
│  │                          TrayIconService, WindowService, UpdateService, UiThemeManager
│  ├─ Interop/                DesktopPinning(바탕화면 고정), MonitorMetrics(모니터 해상도·배율), Occlusion(가림 감지),
│  │                          ShellFiles·ShellIcons·ShellContextMenu (파일 위젯의 셸 기능)
│  ├─ Imaging/ImageLoader     사진·GIF 디코딩 (EXIF 회전, GIF 프레임 합성)
│  ├─ Controls/OutlinedText   외곽선 글자
│  ├─ Logging/FileLogger      파일 로그
│  └─ Themes/                 공통 스타일(Styles.xaml), 설정 창 색상(Light.xaml, Dark.xaml)
├─ tests/MoniMS.Core.Tests    xUnit 단위 테스트
└─ .github/workflows          CI(빌드·테스트), Release(태그 → 설치 파일 → GitHub Release)
```

**설계 원칙**

- Core는 WPF를 모릅니다. UI가 필요한 부분은 인터페이스로 뒤집었습니다. 예를 들어 프리셋은 위젯을 `IWidgetLayoutHost`·`IImageWidgetHost`·`IFileWidgetHost`·`IMediaWidgetHost`로 다루고, App의 위젯 컨트롤러가 이를 구현합니다.
- Windows에 닿는 서비스는 인터페이스(`IWallpaperService`, `IThemeService`, `IDesktopIconService` …)와 DI로 연결되어 있어서, 테스트에서 가짜 구현으로 바꿀 수 있습니다. 파일 위치를 쓰는 클래스(`PresetStore`, `ImageStore` …)는 테스트에서 임시 폴더를 받습니다.
- 프리셋을 적용할 때 한 항목이 실패해도 나머지는 계속 적용되고, 실패한 항목은 경고로 알려 줍니다.
- 위젯 4종과 설정 창은 같은 UI 스레드를 씁니다. 오래 걸리거나 멈출 수 있는 일(프리셋의 바탕화면 적용, 폴더 읽기, 셸 아이콘, 파일 복사·삭제)은 백그라운드나 별도 STA 스레드에서 합니다.

## 데이터 위치

`%APPDATA%\MoniMS\`

| 경로 | 내용 |
|---|---|
| `settings.json` | 위젯 4종의 현재 상태, 마지막 적용 프리셋, 설정 창 테마, 측정 주기(`refreshIntervalMs`, 설정 창에는 없음, 250~10000ms). 읽을 수 없으면 `settings.json.broken`으로 복사해 두고 기본값으로 시작 |
| `presets\{id}\preset.json` | 프리셋 내용 (사람이 읽을 수 있는 JSON) |
| `presets\{id}\wallpaper_*`, `image_*` | 배경화면(모니터별)과 사진 위젯 이미지의 복사본 (원본이 지워져도 프리셋 유지) |
| `images\img-{해시}.*` | 지금 사진 위젯에 보이는 이미지의 복사본. 이름이 내용 해시라서 같은 사진은 한 번만 저장되고, 다른 사진으로 바꾸면 예전 것은 지움 |
| `logs\moniMS.log` | 로그 (1MB를 넘으면 `moniMS.log.old`로 바꾸고 새로 씀) |

설치 프로그램으로 설치하면 앱 자체는 `%LocalAppData%\MoniMS\`에 들어가고, 위 데이터 폴더는 제거해도 남습니다. 예전 버전(0.4.2까지)에서 테마를 가져온 적이 있으면 `themes\` 폴더가 남아 있을 수 있는데, 지금 버전은 쓰지 않으므로 지워도 됩니다.

`preset.json`에는 `schemaVersion`이 있습니다. 현재 4이고(2에서 사진 위젯, 3에서 파일 위젯, 4에서 음악 위젯 추가), 예전 버전으로 만든 프리셋을 덮어쓰면 그때는 고를 수 없었던 위젯도 함께 저장합니다. `settings.json`은 `version`(현재 5)을 보고, 불러올 때 예전 기본 위젯 크기를 지금 기본값으로 바꿉니다.

## 동작 원리 메모

| 기능 | 방식 |
|---|---|
| 배경화면 | `IDesktopWallpaper` COM: 모니터별 이미지, 맞춤 방식, 단색 배경. 원본 파일이 지워졌으면 Windows가 보관하는 변환본(`TranscodedWallpaper`)을 복사. 적용할 때는 모니터 ID → 순번 → 첫 이미지 순으로 짝지음 |
| 테마색 | `HKCU\...\Themes\Personalize`, `DWM`, `Explorer\Accent`, `Control Panel\Desktop`(배경화면에서 강조색 자동 선택) 레지스트리 값을 쓰고 `WM_SETTINGCHANGE("ImmersiveColorSet")` 등을 브로드캐스트. 브로드캐스트는 모든 창의 응답을 기다려 수 초가 걸릴 수 있어서, 이미 같은 상태면 건너뜀 |
| 아이콘 배치 | 바탕화면 `SysListView32`에 `LVM_GETITEMPOSITION`/`LVM_SETITEMPOSITION` 전송. explorer 프로세스에 원격 버퍼를 할당해 사용. 아이콘은 이름으로 짝짓고(같은 이름은 순서대로), 해상도가 바뀌면 비율로 보정. 이미 그 자리에 있는 아이콘은 건너뜀 |
| 위젯 고정 | 창 소유자를 `Progman`으로 지정해 Win+D에도 유지. `WM_WINDOWPOSCHANGING`을 가로채 항상 맨 아래에 둠. 클릭 통과는 `WS_EX_LAYERED` + `WS_EX_TRANSPARENT`이고, WPF가 확장 스타일을 덮어쓰지 않도록 `WM_STYLECHANGING`에서 다시 강제. explorer가 재시작되면(`TaskbarCreated`) 창을 다시 만들고, 소유자와 함께 창이 없어졌으면 3초 뒤 다시 만듦 |
| 위젯 자석 | 편집 모드에서 끌 때(`DragMove`의 이동 루프) 오는 `WM_MOVING`에서 갈 자리를 고침 (`WidgetSnapping`, 계산은 Core의 `WidgetSnap`). 다른 위젯과 위·아래·왼쪽·오른쪽 끝 맞추기, 옆·위·아래에 12 DIP 띄워 붙이기(그 위젯과 겹쳐 있을 때만), 작업 영역 가장자리에서 24 DIP. 16 DIP 안으로 오면 붙고 가로·세로를 따로 정함. 정보 위젯은 창 안쪽 8 DIP에 카드가 있어서 카드끼리 맞춤. 크기 조절에는 적용하지 않음 |
| 위젯 크기 | 정보 위젯은 기준 화면(2880×1800, 배율 200%)에서 차지하는 비율을 어느 해상도·배율에서나 유지 (`WidgetSizing`, `MonitorMetrics`). 해상도·배율이 바뀌거나 다른 모니터로 옮기면 다시 맞춤 |
| 위젯 그리기 | 투명(레이어드) 창은 GPU로 그려도 매번 CPU로 읽어 와야 해서, 위젯 창은 소프트웨어 렌더링(`RenderMode.SoftwareOnly`)을 씀 |
| 사진 위젯 | 고른 사진은 `ImageStore`에 복사해서 씀. 디코딩은 백그라운드에서 하고, 파일을 메모리로 읽어 잠그지 않음. EXIF 회전을 반영하고 큰 사진은 줄여서 디코딩. 크기 조절은 `WS_THICKFRAME` 없이 직접 처리 (화면 가장자리 스냅으로 비율이 깨지지 않게) |
| GIF 재생 | 프레임마다 위치·투명색·disposal을 반영한 완성 프레임을 미리 만들고 창 픽셀 크기에 맞춰 둠 (재생 중 보간 비용 절약). 재생하는 동안 0.5초마다 불투명한 창 하나가 위젯을 완전히 덮었는지 확인(`Occlusion`)해서, 덮였거나 화면이 잠기면 멈춤 |
| 파일 위젯 | 폴더 내용은 `FileSystemEnumerable`(백그라운드), 변경 감시는 `FileSystemWatcher`(창이 보일 때만, 알림을 0.3초 모아서 다시 읽음). 숨김 항목은 파일 탐색기 설정(`SHGetSetSettings`)을 따름. 아이콘은 `SHGetFileInfo`(전용 STA 스레드, 확장자별 캐시). 복사·이동·삭제는 `SHFileOperation`(Windows 진행률·충돌·휴지통 창, 별도 STA 스레드), "Show more options"는 셸 `IContextMenu`. 키보드를 받도록 이 창만 `WS_EX_NOACTIVATE`를 뺌. 네트워크 폴더는 UI 스레드에서 있는지 묻지 않음 (연결이 끊긴 공유는 응답에 수십 초가 걸림) |
| 음악 위젯 | `GlobalSystemMediaTransportControlsSessionManager`(작업표시줄 음량 플라이아웃과 같은 정보). 세션·재생 상태·곡 정보 이벤트는 UI 스레드로 넘겨 처리하고, 위젯이 숨었고 설정 창도 닫혀 있으면 구독을 끊음. 보여 줄 세션: 방금 재생을 시작한 앱 → 위젯에서 고른 앱 → 지금 보이는 앱(일시 정지해도 유지) → Windows가 현재로 보는 앱. `GetCurrentSession`은 목록과 다른 래퍼 객체를 주므로 앱 ID로 짝지음. 앱은 위치를 가끔만 알려 주므로 재생 중이면 그 뒤 흐른 시간을 더해 추정하고, 시간 막대는 1초마다 움직이되 위젯이 다른 창에 가려져 있으면 다시 그리지 않음. 시간 막대로 옮기기는 앱이 허락할 때만(`IsPlaybackPositionEnabled`) `TryChangePlaybackPositionAsync`. 앱이 새 위치를 알려 줄 때까지(최대 3초) 옮긴 위치를 써서 막대가 되돌아가지 않게 함. 음량은 Core Audio: 앱 모드는 기본 출력 장치의 소리 세션 중 프로세스 이름이 같은 것 전부(`ISimpleAudioVolume`, 볼륨 믹서와 같은 값), Windows 모드는 `IAudioEndpointVolume` |
| CPU | `Processor Information\% Processor Utility` (작업 관리자와 같은 값). 없으면 `Processor\% Processor Time` |
| GPU | `GPU Engine` 성능 카운터를 엔진별로(프로세스 합산) 더한 뒤 최댓값 (작업 관리자 방식). 인스턴스가 수백 개라서 틱마다 카테고리를 한 번만 읽고 이전 샘플과의 차이로 직접 계산. 메모리는 `GPU Adapter Memory`의 전용 사용량이고, 전용 사용량이 거의 없는 내장 GPU는 공유 사용량 |
| 네트워크 | 기본 게이트웨이가 있는 활성 어댑터(여럿이면 가장 빠른 것)를 주 네트워크로 봄. 네트워크 변경 알림이 오거나 60초가 지나면 다시 고름 |
| 하드웨어 이름 | CPU·메인보드는 레지스트리 `HARDWARE\DESCRIPTION\System\...`에서 읽음 (WMI는 부팅 직후 수 초가 걸림). GPU 이름과 메모리 크기는 디스플레이 드라이버 레지스트리(`qwMemorySize`) |
| OS 이름 | 레지스트리의 ProductName은 Windows 11에서도 "Windows 10"이라서 빌드 번호(22000 이상)로 판단 |

**알려진 제약**

- 아이콘 배치를 쓰려면 바탕화면에서 **아이콘 자동 정렬**을 꺼야 합니다 (우클릭 → 보기).
- '시작/작업표시줄에 강조색 표시'는 Windows 규칙상 시스템이 다크 모드일 때만 적용됩니다.
- CPU·GPU 온도와 팬 속도는 아직 지원하지 않습니다. 추가하려면 LibreHardwareMonitor와 관리자 권한이 필요합니다.

## 릴리스 방법

배포는 [Velopack](https://velopack.io)(설치 프로그램 + 자동 업데이트)과 GitHub Actions로 자동화되어 있습니다.

### 처음 한 번만

`release.ps1`은 git 저장소, GitHub 연결, 첫 커밋 중 하나라도 없으면 아래 1~2를 안내하면서 대신 해 줍니다 (`-RepoUrl https://github.com/<내아이디>/moniMS`로 주소를 미리 줄 수도 있습니다). 직접 하려면:

1. GitHub에 저장소를 만들고 이 폴더를 올립니다.
   ```powershell
   git init
   git add .
   git commit -m "Initial commit"
   git branch -M main
   git remote add origin https://github.com/<내아이디>/moniMS.git
   git push -u origin main
   ```
2. `Directory.Build.props`의 `RepositoryUrl`을 내 저장소 주소로 바꿔 둡니다. 로컬 빌드에서 업데이트 확인할 때만 쓰는 값이고, 릴리스 빌드는 Actions가 자동으로 넣습니다.
3. 작성자 이름(`Bgg`)을 바꾸려면 `LICENSE`, `Directory.Build.props`의 `Authors`·`Copyright`, `release.yml`의 `--packAuthors`를 고칩니다.

### 새 버전 낼 때 (권장: 릴리스 스크립트)

1. 평소에 바뀐 점을 `CHANGELOG.md`의 `## [Unreleased]` 아래에 적어 둡니다. 비워 둬도 됩니다.
2. moniMS 폴더에서 **`release.cmd`를 더블클릭**하거나 `.\release.ps1`을 실행합니다.
3. 번호를 고릅니다: `[1] 버그 수정` / `[2] 기능 추가` / `[3] 큰 변경` / `[4] 직접 입력`. 변경 내용을 확인하고 `y`를 누르면 끝입니다.

스크립트가 하는 일:

- 마지막 정식 버전 태그(시험판 제외)를 보고 다음 버전 번호를 계산합니다. 같거나 더 낮은 번호, 이미 있는 태그는 거부합니다. 첫 릴리스면 CHANGELOG의 가장 위 버전(없으면 0.1.0)을 씁니다.
- `[Unreleased]` 내용을 `## [x.y.z] - 날짜`로 옮깁니다. 그 버전 절을 미리 적어 두었으면 그것을 쓰고, 둘 다 비어 있으면 지난 릴리스 이후 커밋 메시지로 초안을 만듭니다(`e`를 누르면 메모장에서 고칠 수 있습니다). 시험판은 CHANGELOG를 바꾸지 않습니다.
- `dotnet test`를 실행하고, 실패하면 중단합니다.
- 바뀐 파일 목록을 보여 주고 확인을 받은 뒤 커밋 → 태그 → 푸시합니다. **작업 폴더의 모든 변경이 `git add -A`로 릴리스 커밋에 들어가니**, 릴리스에 넣지 않을 변경은 먼저 정리해 두세요.
- GitHub Actions 진행 페이지를 열어 줍니다.

| 명령 | 용도 |
|---|---|
| `.\release.ps1 -Beta` | 시험판 (`0.2.1-beta.1`). 일반 사용자에게는 알림이 가지 않음 |
| `.\release.ps1 -Bump minor` | 묻지 않고 기능 추가 버전으로 (`patch` / `minor` / `major`) |
| `.\release.ps1 1.0.0` | 버전 직접 지정 |
| `.\release.ps1 -DryRun` | 실제로는 아무것도 바꾸지 않고 미리보기 |
| `.\release.ps1 -SkipTests` | 테스트 건너뛰기 |
| `.\release.ps1 -Yes` | 아무것도 묻지 않고 기본값으로 진행 (버전은 버그 수정) |

### 수동으로 할 때

1. `CHANGELOG.md`에 `## [1.2.3] - 날짜` 절을 추가합니다.
2. `git commit -am "Release 1.2.3"` → `git tag v1.2.3` → `git push origin main --tags`
3. Actions의 **Release**가 테스트 → 빌드 → `vpk pack` → GitHub Release 업로드를 합니다. 릴리스 설명은 CHANGELOG의 그 버전 절에서 가져오고, 이전 릴리스를 받아 델타 업데이트 패키지도 만듭니다. 태그에 `-`가 있으면(`v1.1.0-beta.1`) 시험판(pre-release)으로 올립니다.

- 설치된 앱은 시작 15초 뒤 새 버전을 확인하고(실패하면 2분·10분·30분 뒤 다시) 트레이로 알립니다. **Settings → General → Update & restart**로 업데이트합니다. 시험판은 업데이트로 알리지 않고, 업데이트 확인은 설치 프로그램으로 설치한 경우에만 동작합니다(`dotnet run`에서는 꺼져 있음).
- 실패한 태그는 `git tag -d v1.2.3` → `git push origin :refs/tags/v1.2.3`으로 지우고 다시 붙입니다.
- 한 번 공개한 번호를 다른 내용으로 재사용하면 안 됩니다.
- 사용자 PC에 .NET 10 Desktop Runtime이 없으면 설치 프로그램이 자동으로 설치합니다(`--framework net10.0-x64-desktop`).
- 코드 서명 인증서가 생기면 `release.yml`의 `vpk pack`에 `--signTemplate`를 추가합니다.

### 로컬에서 설치 파일 만들어 보기

```powershell
dotnet tool install -g vpk
dotnet publish src/MoniMS.App -c Release -r win-x64 --self-contained false -o publish
vpk pack --packId MoniMS --packVersion 0.0.1 --packDir publish --mainExe MoniMS.exe --framework net10.0-x64-desktop
# → Releases\MoniMS-win-Setup.exe
```

## 로드맵

1. ✅ 정보 위젯, 프리셋 (배경화면 / 테마색 / 아이콘 / 위젯)
2. ❌ 작업표시줄·시작 메뉴 테마 (Windhawk 연동), Windows Terminal·Discord·Komorebi·YASB 테마: 0.1.0~0.4.2에 있었고 그 다음 버전에서 뺌
3. ⬜ 작업표시줄 꾸미기 (Windhawk 없이): 투명·블러·아크릴 (`SetWindowCompositionAttribute`), 정렬, 자동 숨김 → 프리셋에 `Taskbar` 항목 추가
4. ⬜ 커스텀 시작 메뉴: Win 키를 가로채(저수준 키보드 훅) 직접 디자인한 런처를 띄움 (앱 목록, 고정, 검색)
5. ⬜ 위젯 확장: 온도 센서, 여러 위젯, 위젯별 프리셋 (새 위젯으로 사진·파일·음악 위젯은 추가됨)
6. ✅ 설치 프로그램 + 자동 업데이트 (Velopack), GitHub Actions 릴리스
7. ⬜ 코드 서명 (SignPath 등), winget 등록, Microsoft Store(MSIX — 레지스트리 가상화 대응 필요)
