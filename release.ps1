<#
.SYNOPSIS
  MoniMS 새 버전 릴리스를 한 번에: 버전 결정 → CHANGELOG 정리 → 테스트 → 커밋 → 태그 → 푸시.
  푸시가 끝나면 GitHub Actions가 설치 파일을 만들어 Release에 올립니다.

.EXAMPLE
  .\release.ps1                  # 대화형: 버그 수정 / 기능 추가 / 큰 변경 중에서 고르기
.EXAMPLE
  .\release.ps1 0.2.0            # 버전을 직접 지정
.EXAMPLE
  .\release.ps1 -Bump minor      # 기능 추가 버전으로 바로
.EXAMPLE
  .\release.ps1 -Beta            # 시험판 (예: 0.2.0-beta.1). 일반 사용자에게는 업데이트 알림이 가지 않음
.EXAMPLE
  .\release.ps1 -DryRun          # 실제로는 아무것도 바꾸지 않고 무엇을 할지만 보여줌

.EXAMPLE
  .\release.ps1 -RepoUrl https://github.com/me/moniMS   # 처음 실행: GitHub 연결까지 자동

.NOTES
  처음 실행하면 git 저장소 만들기, GitHub 연결, 첫 업로드까지 안내하면서 대신 해 줍니다.
  변경 내용은 평소에 CHANGELOG.md 의 "## [Unreleased]" 아래에 적어 두면 됩니다.
  비어 있으면 지난 릴리스 이후의 커밋 메시지로 자동으로 채워 줍니다.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Version,
    [ValidateSet('patch', 'minor', 'major')][string]$Bump,
    [switch]$Beta,
    [switch]$DryRun,
    [switch]$SkipTests,
    [switch]$Yes,
    [string]$RepoUrl
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # git 출력(한글 커밋 메시지)을 UTF-8로 읽기
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false
$ChangelogPath = Join-Path $PSScriptRoot 'CHANGELOG.md'

function Write-Step([string]$Message) { Write-Host ""; Write-Host "==> $Message" -ForegroundColor Cyan }
function Write-Ok([string]$Message) { Write-Host "    $Message" -ForegroundColor Green }
function Write-Info([string]$Message) { Write-Host "    $Message" }
function Stop-Release([string]$Message) {
    Write-Host ""
    Write-Host "[중단] $Message" -ForegroundColor Red
    exit 1
}
function Confirm-Step([string]$Question) {
    if ($Yes) { return $true }
    $answer = Read-Host "$Question (y/N)"
    return $answer -match '^(y|yes|ㅛ)$'
}
function Invoke-Native {
    # Windows PowerShell 5.1은 외부 프로그램의 stderr 출력을 오류로 바꿔서
    # ErrorActionPreference=Stop 이면 스크립트가 멈춘다. 그래서 잠시 Continue로 실행한다.
    param([string]$Exe, [string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    return [pscustomobject]@{ Code = $code; Output = (@($out) -join "`n").Trim() }
}
function Invoke-Git {
    # 실패하면 중단, 성공하면 출력 반환
    $r = Invoke-Native 'git' $args
    if ($r.Code -ne 0) { Stop-Release ("git " + ($args -join ' ') + " 실패:`n" + $r.Output) }
    return $r.Output
}
function Test-Git { return ((Invoke-Native 'git' $args).Code -eq 0) }
function Get-GitLines {
    $r = Invoke-Native 'git' $args
    if ($r.Code -ne 0 -or -not $r.Output) { return @() }
    return @($r.Output -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}
function Move-Workflows {
    # 원격 도구가 .github 폴더에 못 써서 따로 둔 워크플로 파일을 제자리로
    $staging = Join-Path $PSScriptRoot '_move-to-.github-workflows'
    $target = Join-Path $PSScriptRoot '.github\workflows'
    if (Test-Path -LiteralPath $staging) {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Get-ChildItem -LiteralPath $staging -Filter *.yml | Move-Item -Destination $target -Force
        Remove-Item -LiteralPath $staging -Recurse -Force
        Write-Ok "워크플로 파일을 .github\workflows 로 옮겼어요."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $target 'release.yml'))) {
        Write-Host "    [주의] .github\workflows\release.yml 이 없어요. 태그는 올라가도 설치 파일은 만들어지지 않아요." -ForegroundColor Yellow
    }
}
function Initialize-Repository {
    Write-Step "처음 설정: GitHub 연결"
    if (-not (Test-Git rev-parse --is-inside-work-tree)) {
        Write-Info "이 폴더는 아직 git 저장소가 아니에요. 지금 만들게요."
        if (-not (Test-Git init -b main)) { Invoke-Git init | Out-Null; Invoke-Git checkout -b main | Out-Null }
        Write-Ok "git 저장소를 만들었어요."
    }

    # 커밋 작성자 이름/이메일 (없으면 이 저장소에만 설정)
    if (-not (Get-GitLines config user.name)) {
        $name = if ($Yes) { 'MoniMS' } else { Read-Host "    커밋에 표시할 이름 (예: Bgg)" }
        Invoke-Git config user.name $name | Out-Null
    }
    if (-not (Get-GitLines config user.email)) {
        $email = if ($Yes) { 'moniMS@users.noreply.github.com' } else { Read-Host "    GitHub 계정 이메일" }
        Invoke-Git config user.email $email | Out-Null
    }

    if (-not (Get-GitLines remote get-url origin)) {
        $url = $RepoUrl
        if (-not $url) {
            Write-Host ""
            Write-Info "1) GitHub에서 빈 저장소를 만드세요. (README, .gitignore, 라이선스는 추가하지 마세요)"
            if (Confirm-Step "   https://github.com/new 를 브라우저로 열까요?") { Start-Process 'https://github.com/new' }
            $url = Read-Host "    2) 만든 저장소 주소를 붙여넣으세요 (예: https://github.com/아이디/moniMS)"
        }
        $url = $url.Trim().TrimEnd('/') -replace '\.git$', ''
        if ($url -notmatch '^https://github\.com/[^/\s]+/[^/\s]+$') { Stop-Release "주소 형식이 이상해요: '$url'" }
        Invoke-Git remote add origin "$url.git" | Out-Null
        Write-Ok "GitHub 저장소 연결: $url"

        # 업데이트 확인용 주소도 같이 맞춰 둔다
        $props = Join-Path $PSScriptRoot 'Directory.Build.props'
        $text = [System.IO.File]::ReadAllText($props)
        $newText = [regex]::Replace($text, '<RepositoryUrl>[^<]*</RepositoryUrl>', "<RepositoryUrl>$url</RepositoryUrl>")
        if ($newText -ne $text) { [System.IO.File]::WriteAllText($props, $newText, $Utf8NoBom); Write-Ok "Directory.Build.props 의 저장소 주소를 바꿨어요." }
    }

    Move-Workflows

    if (-not (Test-Git rev-parse --verify HEAD)) {
        Invoke-Git add -A | Out-Null
        Invoke-Git commit -m "Initial commit" | Out-Null
        Write-Ok "첫 커밋을 만들었어요."
    }
    Write-Info "GitHub에 올리는 중... (처음이면 GitHub 로그인 창이 뜰 수 있어요)"
    Invoke-Git push -u origin HEAD | Out-Null
    Write-Ok "GitHub에 올렸어요."
}
function ConvertTo-VersionParts([string]$Text) {
    if ($Text -notmatch '^(\d+)\.(\d+)\.(\d+)') { return $null }
    return @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
}

# ------------------------------------------------------------------
Write-Step "환경 확인"
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Stop-Release "git이 설치되어 있지 않아요. https://git-scm.com 에서 설치하세요." }
if (-not $SkipTests -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-Release ".NET SDK가 없어요. https://dot.net 에서 .NET 10 SDK를 설치하세요." }
$isRepo = Test-Git rev-parse --is-inside-work-tree
$hasRemote = $isRepo -and [bool](Get-GitLines remote get-url origin)
$hasCommit = $isRepo -and (Test-Git rev-parse --verify HEAD)
if (-not ($isRepo -and $hasRemote -and $hasCommit)) {
    if ($DryRun) { Stop-Release "아직 GitHub과 연결되지 않았어요. -DryRun 없이 실행하면 연결부터 도와드려요." }
    Initialize-Repository
}
$remoteUrl = (Get-GitLines remote get-url origin | Select-Object -First 1)
$branch = (Get-GitLines rev-parse --abbrev-ref HEAD | Select-Object -First 1)
Write-Ok "저장소: $remoteUrl"
Write-Ok "브랜치: $branch"
if ($branch -ne 'main' -and -not (Confirm-Step "지금 브랜치가 main이 아니에요($branch). 그래도 릴리스할까요?")) { Stop-Release "취소했어요." }

Write-Info "GitHub에서 태그 목록 가져오는 중..."
Invoke-Native 'git' @('fetch', '--tags', '--quiet', 'origin') | Out-Null

# ------------------------------------------------------------------
Write-Step "버전 정하기"
$tags = @(Get-GitLines tag --list 'v*')
$stable = $tags | Where-Object { $_ -notmatch '-' } |
    Sort-Object { $p = ConvertTo-VersionParts $_.TrimStart('v'); if ($p) { '{0:D5}.{1:D5}.{2:D5}' -f $p[0], $p[1], $p[2] } else { '' } } |
    Select-Object -Last 1
$changelog = [System.IO.File]::ReadAllText($ChangelogPath)

if ($stable) {
    $current = $stable.TrimStart('v')
    $parts = ConvertTo-VersionParts $current
    $choices = [ordered]@{
        patch = '{0}.{1}.{2}' -f $parts[0], $parts[1], ($parts[2] + 1)
        minor = '{0}.{1}.0' -f $parts[0], ($parts[1] + 1)
        major = '{0}.0.0' -f ($parts[0] + 1)
    }
    Write-Info "마지막 릴리스: $current"
}
else {
    # 첫 릴리스: CHANGELOG에 적어 둔 가장 위 버전(없으면 0.1.0)
    $first = [regex]::Match($changelog, '(?m)^## \[(\d+\.\d+\.\d+)\]')
    $current = $null
    $choices = [ordered]@{ first = $(if ($first.Success) { $first.Groups[1].Value } else { '0.1.0' }) }
    Write-Info "아직 릴리스가 없어요. 첫 릴리스예요."
}

if (-not $Version) {
    if ($Bump) {
        if (-not $choices.Contains($Bump)) { Stop-Release "첫 릴리스에는 -Bump를 쓸 수 없어요. 버전을 직접 적거나 그냥 실행하세요." }
        $Version = $choices[$Bump]
    }
    elseif ($choices.Contains('first')) {
        $Version = $choices['first']
        if (-not $Yes) {
            $typed = Read-Host "첫 버전 번호 (Enter = $Version)"
            if ($typed) { $Version = $typed.Trim().TrimStart('v') }
        }
    }
    else {
        Write-Host ""
        Write-Host "    [1] 버그 수정   $($choices.patch)"
        Write-Host "    [2] 기능 추가   $($choices.minor)"
        Write-Host "    [3] 큰 변경     $($choices.major)"
        Write-Host "    [4] 직접 입력"
        $pick = if ($Yes) { '1' } else { Read-Host "    번호를 고르세요 (Enter = 1)" }
        switch ($pick) {
            '' { $Version = $choices.patch }
            '1' { $Version = $choices.patch }
            '2' { $Version = $choices.minor }
            '3' { $Version = $choices.major }
            '4' { $Version = (Read-Host "    버전 (예: 1.2.3)").Trim().TrimStart('v') }
            default { Stop-Release "1~4 중에서 골라 주세요." }
        }
    }
}
$Version = $Version.Trim().TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Stop-Release "버전은 1.2.3 형식이어야 해요: '$Version'" }

if ($current) {
    $a = ConvertTo-VersionParts $Version; $b = ConvertTo-VersionParts $current
    $newer = ($a[0] -gt $b[0]) -or ($a[0] -eq $b[0] -and $a[1] -gt $b[1]) -or ($a[0] -eq $b[0] -and $a[1] -eq $b[1] -and $a[2] -gt $b[2])
    if (-not $newer) { Stop-Release "새 버전($Version)은 마지막 릴리스($current)보다 커야 해요." }
}

$FullVersion = $Version
if ($Beta) {
    $n = 1
    while ($tags -contains "v$Version-beta.$n") { $n++ }
    $FullVersion = "$Version-beta.$n"
}
$Tag = "v$FullVersion"
if ($tags -contains $Tag) { Stop-Release "태그 $Tag 는 이미 있어요. 한 번 공개한 번호는 다시 쓰면 안 돼요." }
Write-Ok "이번 버전: $FullVersion  (태그 $Tag)"

# ------------------------------------------------------------------
Write-Step "CHANGELOG 정리"
$today = Get-Date -Format 'yyyy-MM-dd'
$nl = if ($changelog.Contains("`r`n")) { "`r`n" } else { "`n" }   # 파일의 줄바꿈 방식 유지
$newChangelog = $changelog
$notes = ''

$existing = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($Version))\][^\n]*\n(.*?)(?=^## |\z)")
$unreleased = [regex]::Match($changelog, '(?ms)^## \[Unreleased\][^\n]*\n(.*?)(?=^## |\z)')

if ($Beta) {
    Write-Info "시험판은 CHANGELOG를 바꾸지 않아요 (정식 버전 낼 때 정리)."
    $notes = if ($unreleased.Success) { $unreleased.Groups[1].Value.Trim() } else { '' }
}
elseif ($existing.Success -and $existing.Groups[1].Value.Trim()) {
    Write-Info "이미 적어 둔 [$Version] 내용을 사용해요."
    $notes = $existing.Groups[1].Value.Trim()
}
else {
    if ($unreleased.Success -and $unreleased.Groups[1].Value.Trim()) {
        Write-Info "[Unreleased] 아래에 적어 둔 내용을 $Version 으로 옮겨요."
        $notes = $unreleased.Groups[1].Value.Trim()
    }
    else {
        # 적어 둔 게 없으면 커밋 메시지로 초안 만들기
        $range = if ($stable) { "$stable..HEAD" } else { 'HEAD' }
        $subjects = @(Get-GitLines log $range --pretty=format:%s |
            Where-Object { $_ -notmatch '^(Release |Merge |Initial commit)' } | Select-Object -Unique)
        if ($subjects.Count -eq 0) { $subjects = @('Maintenance update') }
        $notes = "### Changes$nl" + (($subjects | ForEach-Object { "- $_" }) -join $nl)
        Write-Info "적어 둔 내용이 없어서 커밋 메시지로 초안을 만들었어요."
    }

    Write-Host ""
    Write-Host ($notes -split "`n" | ForEach-Object { "      $_" } | Out-String) -ForegroundColor Gray
    if (-not $Yes -and -not $DryRun) {
        $edit = Read-Host "    이대로 쓸까요? (Enter = 사용 / e = 메모장에서 고치기)"
        if ($edit -match '^(e|ㄷ)$') {
            $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "moniMS-release-notes-$Version.md"
            [System.IO.File]::WriteAllText($tmp, $notes, $Utf8NoBom)
            Start-Process notepad.exe -ArgumentList "`"$tmp`"" -Wait
            $notes = [System.IO.File]::ReadAllText($tmp).Trim()
            Remove-Item $tmp -ErrorAction SilentlyContinue
            if (-not $notes) { Stop-Release "변경 내용이 비어 있어요." }
        }
    }

    $notes = ($notes -replace "`r?`n", $nl)
    $section = "## [$Version] - $today$nl$nl$notes$nl$nl"
    if ($existing.Success) {
        $newChangelog = $changelog.Remove($existing.Index, $existing.Length).Insert($existing.Index, $section)
    }
    elseif ($unreleased.Success) {
        # [Unreleased] 는 비워서 남겨 두고 바로 아래에 새 버전 절 추가
        $newChangelog = $changelog.Remove($unreleased.Index, $unreleased.Length).Insert($unreleased.Index, "## [Unreleased]$nl$nl$section")
    }
    else {
        $firstSection = [regex]::Match($changelog, '(?m)^## ')
        $at = if ($firstSection.Success) { $firstSection.Index } else { $changelog.Length }
        $newChangelog = $changelog.Insert($at, "## [Unreleased]$nl$nl$section")
    }
}

# ------------------------------------------------------------------
if (-not $SkipTests) {
    Write-Step "테스트 실행 (dotnet test)"
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & dotnet test MoniMS.sln -c Release --nologo -v quiet
    $testCode = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($testCode -ne 0) { Stop-Release "테스트가 실패했어요. 고친 뒤 다시 실행하세요." }
    Write-Ok "테스트 통과"
}

# ------------------------------------------------------------------
Write-Step "확인"
Move-Workflows
$pending = @(Get-GitLines status --porcelain)
Write-Info "버전     : $FullVersion"
Write-Info "태그     : $Tag"
Write-Info "커밋할 파일: $($pending.Count)개 $(if (-not $Beta) { '(+ CHANGELOG.md)' })"
foreach ($line in ($pending | Select-Object -First 15)) { Write-Host "      $line" -ForegroundColor DarkGray }
if ($pending.Count -gt 15) { Write-Host "      ... 외 $($pending.Count - 15)개" -ForegroundColor DarkGray }

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] 여기까지만 확인했어요. 실제로 바뀐 것은 없어요." -ForegroundColor Yellow
    exit 0
}
if (-not (Confirm-Step "    GitHub에 올려서 릴리스할까요?")) { Stop-Release "취소했어요. 바뀐 것은 없어요." }

# ------------------------------------------------------------------
Write-Step "커밋 · 태그 · 푸시"
if ($newChangelog -ne $changelog) { [System.IO.File]::WriteAllText($ChangelogPath, $newChangelog, $Utf8NoBom) }
Invoke-Git add -A | Out-Null
if (-not (Test-Git diff --cached --quiet)) {
    Invoke-Git commit -m "Release $FullVersion" | Out-Null
    Write-Ok "커밋: Release $FullVersion"
}
else {
    Write-Info "커밋할 변경이 없어서 현재 커밋에 태그만 붙여요."
}
Invoke-Git tag -a $Tag -m "MoniMS $FullVersion" | Out-Null
Write-Ok "태그: $Tag"
Invoke-Git push origin HEAD | Out-Null
Invoke-Git push origin $Tag | Out-Null
Write-Ok "푸시 완료"

# ------------------------------------------------------------------
$web = $remoteUrl -replace '^git@github\.com:', 'https://github.com/' -replace '\.git$', ''
Write-Host ""
Write-Host "완료! 5~10분 뒤 설치 파일이 Release에 올라가요." -ForegroundColor Green
Write-Host "  진행 상황: $web/actions"
Write-Host "  릴리스   : $web/releases/tag/$Tag"
if (-not $Yes -and (Confirm-Step "  진행 상황 페이지를 브라우저로 열까요?")) { Start-Process "$web/actions" }
