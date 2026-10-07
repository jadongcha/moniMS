# MoniMS 개발 문서

사용자용 안내는 [README.md](../README.md) / [README.ko.md](../README.ko.md)를 보세요. 이 문서는 코드를 고치거나 릴리스하는 사람을 위한 문서입니다.

## 기능 요약

- **프리셋**: 배경화면(모니터별), 테마색, 바탕화면 아이콘 배치, 정보 위젯 레이아웃을 한 번에 저장하고 전환합니다.
- **정보 위젯**: 바탕화면에 붙어 있는 시스템 정보 위젯입니다. CPU·메모리·GPU 사용량과 그래프, 디스크, IP·네트워크 속도, Windows 버전, 하드웨어 정보를 보여 줍니다.
- UI는 영어, 글꼴은 **JetBrains Mono**를 앱에 내장했습니다 (SIL OFL 1.1). 위젯 글꼴은 설정에서 바꿀 수 있고, 프리셋에도 함께 저장됩니다.
- **테마**: 인터넷의 테마 패키지(GitHub 링크, .zip, 폴더, .json, .css)를 가져와 작업표시줄·시작 메뉴(Windhawk), Windows Terminal, Discord(Vencord/BetterDiscord 등)에 적용합니다. 적용 전 상태는 자동으로 백업되고 되돌릴 수 있습니다.

## 실행 방법

필요한 것: Windows 11, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json`이 10.0.100 이상을 요구), 그리고 Visual Studio 2026 / 2022 최신판 또는 VS Code + C# Dev Kit.

```powershell
cd moniMS
dotnet run --project src/MoniMS.App      # 실행
dotnet test                              # 단위 테스트
dotnet publish src/MoniMS.App -c Release -r win-x64 --self-contained false -o publish   # 배포용 빌드 (릴리스는 아래 "릴리스 방법" 참고)
```

Visual Studio에서는 `MoniMS.sln`을 열고 **MoniMS.App**을 시작 프로젝트로 지정한 뒤 F5를 누르면 됩니다.

실행하면 알림 영역(트레이)에 아이콘이 생깁니다. 트레이 메뉴에서 할 수 있는 일:

- 프리셋 적용
- 현재 상태를 프리셋으로 바로 저장
- 위젯 표시/숨기기, 위치 편집 (위젯은 항상 클릭 통과, 편집 모드에서만 잡을 수 있음)
- 설정 창 열기

`--background` 인자를 붙이면 설정 창 없이 트레이로만 시작합니다. Windows 시작 시 자동 실행이 이 방식을 씁니다.

## 프로젝트 구조

```
MoniMS.sln
├─ src/MoniMS.Core            UI와 무관한 로직 (테스트 가능)
│  ├─ SystemInfo/             CPU·GPU·메모리·네트워크·디스크·OS·하드웨어 정보 수집
│  ├─ Presets/                프리셋 모델, JSON 저장소, PresetService (캡처/적용)
│  ├─ Desktop/                배경화면(COM), 테마(레지스트리), 아이콘 배치(ListView), 색상 유틸
│  ├─ Settings/               앱 설정, 자동 실행 등록
│  ├─ Shell/                  테마: Windhawk 저장소, 테마 가져오기/분석/적용/백업
│  │  └─ Apps/                Windows Terminal·Discord 테마 적용 (FileJournal로 원래 파일 내용 기록)
│  └─ Interop/                Win32 P/Invoke, COM 인터페이스
├─ src/MoniMS.App             WPF 앱 (MVVM: CommunityToolkit.Mvvm, DI: Microsoft.Extensions.DependencyInjection)
│  ├─ Program.cs              시작점: Velopack 처리 → WPF 앱 실행
│  ├─ Views/                  WidgetWindow(위젯), ManagerWindow(설정), InputDialog
│  ├─ ViewModels/             위젯 구역별 ViewModel, 설정 ViewModel
│  ├─ Services/               WidgetController, TrayIconService, WindowService, UpdateService,
│  │                          ElevatedWindhawkWriter (관리자 권한 헬퍼), UiThemeManager
│  ├─ Interop/DesktopPinning  위젯을 바탕화면에 고정
│  └─ Themes/Styles.xaml      공통 스타일
├─ tests/MoniMS.Core.Tests    xUnit 단위 테스트
└─ .github/workflows          CI(빌드·테스트), Release(태그 → 설치 파일 → GitHub Release)
```

**설계 원칙**

- Core는 WPF를 모릅니다. UI가 필요한 부분은 인터페이스로 뒤집었습니다. 예를 들어 위젯 레이아웃은 `IWidgetLayoutHost`로 다룹니다.
- 서비스는 모두 인터페이스(`IWallpaperService`, `IThemeService` …)와 DI로 연결되어 있어서, 테스트에서 가짜 구현으로 바꿀 수 있습니다.
- 프리셋을 적용할 때 한 항목이 실패해도 나머지는 계속 적용되고, 실패한 항목은 경고로 알려 줍니다.

## 데이터 위치

`%APPDATA%\MoniMS\`

| 경로 | 내용 |
|---|---|
| `settings.json` | 현재 위젯 레이아웃, 마지막 적용 프리셋 |
| `presets\{id}\preset.json` | 프리셋 내용 (사람이 읽을 수 있는 JSON) |
| `presets\{id}\wallpaper_*.jpg` | 배경화면 복사본 (원본이 지워져도 프리셋 유지) |
| `themes\{id}\` | 가져온 테마 (`theme.json` + `files\`) |
| `themes\_backups\*.json` | 테마 적용 전 백업 (Windhawk 설정 + Terminal·Discord 파일 원래 내용) |
| `logs\moniMS.log` | 로그 |

설치 프로그램으로 설치하면 앱 자체는 `%LocalAppData%\MoniMS\`에 들어가고, 위 데이터 폴더는 제거해도 남습니다.

## 동작 원리 메모

| 기능 | 방식 |
|---|---|
| 배경화면 | `IDesktopWallpaper` COM: 모니터별 이미지, 맞춤 방식, 단색 배경 |
| 테마색 | `HKCU\...\Themes\Personalize`, `DWM`, `Explorer\Accent` 레지스트리 값을 쓰고 `WM_SETTINGCHANGE("ImmersiveColorSet")`를 브로드캐스트 |
| 아이콘 배치 | 바탕화면 `SysListView32`에 `LVM_GETITEMPOSITION`/`LVM_SETITEMPOSITION` 전송. explorer 프로세스에 원격 버퍼를 할당해 사용. 해상도가 바뀌면 비율로 보정 |
| 위젯 고정 | 창 소유자를 `Progman`으로 지정해 Win+D에도 유지. `WM_WINDOWPOSCHANGING`을 가로채 항상 맨 아래에 둠. explorer가 재시작되면 창을 다시 만듦 |
| CPU | `Processor Information\% Processor Utility` (작업 관리자와 같은 값) |
| GPU | `GPU Engine` 성능 카운터를 엔진별로 합산한 뒤 최댓값 (작업 관리자 방식). VRAM은 `GPU Adapter Memory` |
| OS 이름 | 레지스트리의 ProductName은 Windows 11에서도 "Windows 10"이라서 빌드 번호(22000 이상)로 판단 |

**알려진 제약**

- 아이콘 배치를 쓰려면 바탕화면에서 **아이콘 자동 정렬**을 꺼야 합니다 (우클릭 → 보기).
- '시작/작업표시줄에 강조색 표시'는 Windows 규칙상 시스템이 다크 모드일 때만 적용됩니다.
- CPU·GPU 온도와 팬 속도는 아직 지원하지 않습니다. 추가하려면 LibreHardwareMonitor와 관리자 권한이 필요합니다.

## 작업표시줄·시작 메뉴 테마 (Windhawk 연동)

설정 창의 **Themes** 탭입니다.

Windows 11의 작업표시줄과 시작 메뉴는 공식적으로 바꿀 방법이 없습니다. 그래서 대부분의 테마는 [Windhawk](https://windhawk.net)의 스타일러 모드 설정(JSON) 형태로 배포됩니다. MoniMS는 이 설정을 가져와 Windhawk에 직접 써 넣습니다. explorer에 코드를 주입하는 일은 Windhawk가 합니다.

| 패키지 안의 파일 | 적용 대상 Windhawk 모드 |
|---|---|
| `*Taskbar*.json` | `windows-11-taskbar-styler` |
| `*Start*.json` | `windows-11-start-menu-styler` |
| `*Notification*.json` | `windows-11-notification-center-styler` |
| `iconTheme` 키가 있는 JSON | `icon-resource-redirect` |
| `Wallpaper` 폴더의 이미지 | 배경화면 (선택) |

파일 이름으로 판단이 안 되면 스타일 대상(`*.target`) 내용으로 판단합니다.

- **저장 위치:** `windhawk.ini`의 `[Storage]`를 따릅니다. 설치형은 `HKLM\SOFTWARE\Windhawk\Engine\Mods\{id}\Settings`, 포터블은 `{AppDataPath}\Engine\Mods\{id}.ini`입니다.
- **즉시 반영:** 쓰고 나서 `SettingsChangeTime`을 갱신하면 Windhawk가 바로 반영합니다.
- **관리자 권한:** HKLM에 쓸 권한이 없으면 `MoniMS.exe --windhawk-write`를 관리자 권한으로 한 번 실행합니다(UAC). 이 헬퍼는 위 4개 모드 ID 외의 쓰기 요청은 거부합니다.
- **백업과 복원:** 적용 전 현재 설정을 `%APPDATA%\MoniMS\themes\_backups`에 저장합니다. Restore를 누르면 가장 최근 백업으로 되돌립니다.
- **Reset (원래대로):** 모든 백업을 오래된 순으로 읽어 항목(모드·파일)마다 가장 오래된 값, 즉 테마를 처음 적용하기 전 값으로 되돌리고 백업을 모두 지웁니다. 테마 배경화면을 처음 적용할 때 지금 배경화면을 `_backups\original-wallpaper`에 저장해 두었다가 함께 되돌립니다.
- **임의 스크립트:** 시작 메뉴의 `webContentCustomJs` 같은 스크립트 설정은 기본적으로 빼고 적용하며, 사용자가 허용할 때만 넣습니다.
- **다른 앱 설정:** Spicetify, Komorebi 등 지원하지 않는 앱용 폴더는 "적용 안 됨"으로 표시만 합니다.

## Windows Terminal · Discord 테마

Windhawk 설정이 아닌 파일도 분석합니다 (`Shell/Apps/`). 적용할 때 바꾸는 파일의 원래 내용을 `FileJournal`에 기록해 두고, Windhawk 백업과 같은 백업 파일(`files` 항목)에 함께 저장합니다. Restore를 누르면 원래 내용으로 되돌리고, 원래 없던 파일은 지웁니다.

| 패키지 안의 파일 | 적용 방법 |
|---|---|
| `schemes`가 있는 JSON (Terminal `settings.json`) 또는 색 구성표 하나짜리 JSON | `%LOCALAPPDATA%\Packages\Microsoft.WindowsTerminal*_8wekyb3d8bbwe\LocalState\settings.json`(스토어/Preview)과 비패키지 설치의 `settings.json`에 **합치기** |
| `*.theme.css`, `Discord` 폴더 안의 `.css` | `%APPDATA%\{Vencord, vesktop, Equicord, equibop}\themes`에 복사 + `settings\settings.json`의 `enabledThemes`에 추가, `%APPDATA%\BetterDiscord\themes`에 복사 + `data\{채널}\themes.json`에 `{ "@name": true }` |

- **Terminal 합치기 규칙:** 패키지 settings.json을 통째로 덮어쓰지 않습니다. `schemes`·`themes`는 이름이 같으면 교체하고 없으면 추가합니다. `profiles.defaults`에서는 모양 관련 키만 가져옵니다(colorScheme, font, opacity, useAcrylic, padding, cursorShape …). 단축키, 기본 프로필, 프로필 목록은 그대로 둡니다. 예전 배열 형식의 `profiles`는 `{ defaults, list }`로 바꿉니다. 주석이 있는 JSONC도 읽지만, 다시 쓸 때 주석은 사라집니다(복원하면 원래 파일 그대로 돌아옵니다).
- **글꼴:** `font.face`가 설치돼 있지 않으면(HKLM/HKCU `...\CurrentVersion\Fonts`) 글꼴만 빼고 적용하고 경고를 띄웁니다. Discord 테마의 `--font` 변수도 확인합니다.
- **Discord:** 순정 Discord는 테마를 불러올 수 없어서, 이미 설치된 클라이언트 모드가 있을 때만 적용합니다. MoniMS는 클라이언트 모드를 설치하지 않습니다(Discord 이용약관 위반 소지). Discord가 실행 중이면 재시작하라고 알립니다.
- **예전 매니페스트:** `theme.json`의 `schemaVersion`이 2보다 낮으면 불러올 때 다시 분석해 `apps`를 채웁니다.

## 릴리스 방법

배포는 [Velopack](https://velopack.io)(설치 프로그램 + 자동 업데이트)과 GitHub Actions로 자동화되어 있습니다.

### 처음 한 번만

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
3. `LICENSE`, `README.md`의 `Bgg`와 `OWNER` 부분을 내 이름·아이디로 바꿉니다.

### 새 버전 낼 때 (권장: 릴리스 스크립트)

1. 평소에 바뀐 점을 `CHANGELOG.md`의 `## [Unreleased]` 아래에 적어 둡니다. 비워 둬도 됩니다.
2. moniMS 폴더에서 **`release.cmd`를 더블클릭**하거나 `.\release.ps1`을 실행합니다.
3. 번호를 고릅니다: `[1] 버그 수정` / `[2] 기능 추가` / `[3] 큰 변경` / `[4] 직접 입력`. 변경 내용을 확인하고 `y`를 누르면 끝입니다.

스크립트가 하는 일:

- 마지막 태그를 보고 다음 버전 번호를 계산합니다. 한 번 쓴 번호나 더 낮은 번호는 거부합니다.
- `[Unreleased]` 내용을 `## [x.y.z] - 날짜`로 옮깁니다. 비어 있으면 지난 릴리스 이후 커밋 메시지로 초안을 만들고, 메모장에서 고칠 수 있습니다.
- `dotnet test`를 실행하고, 실패하면 중단합니다.
- 바뀐 파일 목록을 보여 준 뒤 커밋 → 태그 → 푸시합니다.
- GitHub Actions 진행 페이지를 열어 줍니다.

| 명령 | 용도 |
|---|---|
| `.\release.ps1 -Beta` | 시험판 (`0.2.1-beta.1`). 일반 사용자에게는 알림이 가지 않음 |
| `.\release.ps1 -Bump minor` | 묻지 않고 기능 추가 버전으로 |
| `.\release.ps1 1.0.0` | 버전 직접 지정 |
| `.\release.ps1 -DryRun` | 실제로는 아무것도 바꾸지 않고 미리보기 |

### 수동으로 할 때

1. `CHANGELOG.md`에 `## [1.2.3] - 날짜` 절을 추가합니다.
2. `git commit -am "Release 1.2.3"` → `git tag v1.2.3` → `git push origin main --tags`
3. Actions의 **Release**가 테스트 → 빌드 → `vpk pack` → GitHub Release 업로드를 합니다. 설치된 앱은 시작 15초 뒤 새 버전을 확인해 트레이로 알리고, **Settings → General → Update & restart**로 업데이트합니다.

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
2. ✅ 작업표시줄·시작 메뉴 테마 (Windhawk 연동), Windows Terminal·Discord 테마
3. ⬜ 작업표시줄 꾸미기 (Windhawk 없이): 투명·블러·아크릴 (`SetWindowCompositionAttribute`), 정렬, 자동 숨김 → 프리셋에 `Taskbar` 항목 추가
4. ⬜ 커스텀 시작 메뉴: Win 키를 가로채(저수준 키보드 훅) 직접 디자인한 런처를 띄움 (앱 목록, 고정, 검색)
5. ⬜ 위젯 확장: 온도 센서, 여러 위젯, 위젯별 프리셋
6. ✅ 설치 프로그램 + 자동 업데이트 (Velopack), GitHub Actions 릴리스
7. ⬜ 코드 서명 (SignPath 등), winget 등록, Microsoft Store(MSIX — 레지스트리 가상화 대응 필요)
