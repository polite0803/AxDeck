#Requires -Version 5.1
<#
.SYNOPSIS
    AxDeck 的本地门禁：构建整个解决方案，并按顺序跑完全部测试套件。

.DESCRIPTION
    和 .github/workflows/ci.yml 跑的检查一致：
      1. dotnet build AxDeck.sln -c Release
      2. tests/AxDeck.Tests          Core 逻辑（数量最多）
      3. tests/AxDeck.MediaTests     播放器桥接
      4. tests/AxDeck.BoxVerify      桌面页端到端（真文件系统，跑完还原注册表）
      5. tests/AxDeck.WindowsTests   WPF 布局与绘制、原生探测

    测试是自写的 console runner，不是 xUnit/NUnit/MSTest，所以这里用
    dotnet run --no-build 调用，`dotnet test` 找不到它们。

    每一步的输出实时打印，同时写入 .artifacts/gate/<时间戳>/<步骤>.log。
    全部跑完打印汇总；任何一步失败，脚本以失败步数作为退出码。

.PARAMETER Hosted
    只跑在 CI 虚拟机上也成立的 Windows 检查（等价于 CI 的 --hosted），
    排除读本机显卡、显示器、温度、功耗、电池与外壳身份的项目。
    在真实开发机上不加这个开关，跑全部检查。

.PARAMETER NoBuild
    跳过构建，直接跑测试（必须已经 build 过，否则 --no-build 会失败）。

.PARAMETER Configuration
    构建配置，默认 Release。

.EXAMPLE
    pwsh -File scripts/Gate.ps1

.EXAMPLE
    pwsh -File scripts/Gate.ps1 -Hosted

.NOTES
    需要 .NET SDK 10 与 PowerShell 5.1+（或 pwsh 7+）。
    退出码 0 = 门禁通过；N = N 个步骤失败。
#>
[CmdletBinding()]
param(
    [switch]$Hosted,
    [switch]$NoBuild,
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'AxDeck.sln'))) {
    throw "找不到 AxDeck.sln：$root"
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $root (Join-Path '.artifacts/gate' $stamp)
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$results = [System.Collections.Generic.List[object]]::new()
$total = [System.Diagnostics.Stopwatch]::StartNew()

function Invoke-GateStep {
    param(
        [string]$Name,
        [string]$FileName,
        [string[]]$Arguments
    )

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    Write-Host ("    {0} {1}" -f $FileName, ($Arguments -join ' ')) -ForegroundColor DarkGray

    $log = Join-Path $logDir (($Name -replace '[^A-Za-z0-9]+', '-').Trim('-') + '.log')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $FileName @Arguments 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
    $watch.Stop()

    $text = Get-Content -Path $log -Raw -ErrorAction SilentlyContinue
    if ($null -eq $text) { $text = '' }

    # MSBuild 的汇总行会随系统语言变化，这里按行首的 “: warning” 数，不受语言影响。
    $warnings = ([regex]::Matches($text, '(?m):\s+warning\s')).Count
    $summary = ($text -split "`r?`n" |
        Where-Object { $_ -match '(?i)(checks? passed|ALL CHECKS PASSED|CHECKS FAILED|Build succeeded|生成成功)' } |
        Select-Object -Last 1)
    if ([string]::IsNullOrWhiteSpace($summary)) { $summary = if ($code -eq 0) { 'ok' } else { "exit $code" } }

    $results.Add([pscustomobject]@{
        Step     = $Name
        ExitCode = $code
        Seconds  = [math]::Round($watch.Elapsed.TotalSeconds, 1)
        Warnings = $warnings
        Summary  = $summary.Trim()
        Log      = $log
    })

    if ($code -ne 0) {
        Write-Host ("    FAILED (exit {0}) after {1:N1}s" -f $code, $watch.Elapsed.TotalSeconds) -ForegroundColor Red
    }
    else {
        Write-Host ("    ok in {0:N1}s" -f $watch.Elapsed.TotalSeconds) -ForegroundColor Green
    }
}

Push-Location $root
try {
    if (-not $NoBuild) {
        Invoke-GateStep -Name 'build' -FileName 'dotnet' `
            -Arguments @('build', 'AxDeck.sln', '-c', $Configuration)
    }

    Invoke-GateStep -Name 'core-tests' -FileName 'dotnet' `
        -Arguments @('run', '--project', 'tests/AxDeck.Tests', '-c', $Configuration, '--no-build')
    Invoke-GateStep -Name 'media-tests' -FileName 'dotnet' `
        -Arguments @('run', '--project', 'tests/AxDeck.MediaTests', '-c', $Configuration, '--no-build')
    Invoke-GateStep -Name 'boxverify-tests' -FileName 'dotnet' `
        -Arguments @('run', '--project', 'tests/AxDeck.BoxVerify', '-c', $Configuration, '--no-build')

    $windowsArgs = @('run', '--project', 'tests/AxDeck.WindowsTests', '-c', $Configuration, '--no-build')
    if ($Hosted) { $windowsArgs += '--hosted' }
    Invoke-GateStep -Name 'windows-tests' -FileName 'dotnet' -Arguments $windowsArgs
}
finally {
    Pop-Location
    $total.Stop()
}

$failed = @($results | Where-Object { $_.ExitCode -ne 0 })
$warnings = ($results | Measure-Object -Property Warnings -Sum).Sum

Write-Host ""
Write-Host "================ GATE SUMMARY ================" -ForegroundColor Cyan
$results | Format-Table -AutoSize Step, ExitCode, Seconds, Warnings, Summary | Out-String | Write-Host
Write-Host ("total {0:N1}s | {1} step(s) | {2} failed | {3} build warning(s)" -f `
    $total.Elapsed.TotalSeconds, $results.Count, $failed.Count, $warnings)

if ($failed.Count -eq 0) {
    Write-Host "GATE PASSED" -ForegroundColor Green
}
else {
    Write-Host ("GATE FAILED: {0}" -f (($failed | ForEach-Object { $_.Step }) -join ', ')) -ForegroundColor Red
    Write-Host "logs: $logDir" -ForegroundColor Yellow
}

exit $failed.Count
