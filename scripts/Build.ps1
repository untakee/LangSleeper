param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$taskRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskDotnet = Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$taskPreviousPath = $env:PATH
$taskPreviousLib = $env:LIB
$taskPreviousTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    if (-not $SkipTests) {
        & $taskDotnet build tests\LangSleeper.Tests\LangSleeper.Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed. Close running development instances before building.' }
        & $taskDotnet tests\LangSleeper.Tests\bin\Release\net8.0-windows\win-x64\LangSleeper.Tests.dll --integration
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }
    $taskPublishOptions = @()
    # Optional local toolchain; standard Visual Studio Build Tools are discovered by .NET otherwise.
    $taskToolsRoot = Join-Path $taskRoot '.tools\Microsoft.VC.14.44.17.14.Tools.HostX64.TargetX64.base'
    if (Test-Path -LiteralPath $taskToolsRoot) {
        $taskLinker = Get-ChildItem -LiteralPath $taskToolsRoot -Recurse -Filter link.exe | Select-Object -First 1
        $taskCrt = Get-ChildItem -LiteralPath (Join-Path $taskRoot '.tools\Microsoft.VC.14.44.17.14.CRT.x64.Desktop.base') -Recurse -Filter libcmt.lib | Where-Object { $_.DirectoryName.EndsWith('\x64') } | Select-Object -First 1
        $taskOldNames = Get-ChildItem -LiteralPath (Join-Path $taskRoot '.tools\crt-store') -Recurse -Filter oldnames.lib | Where-Object { $_.DirectoryName.EndsWith('\x64') } | Select-Object -First 1
        $env:PATH = $taskLinker.DirectoryName + ';' + $env:PATH
        $env:LIB = $taskCrt.DirectoryName + ';' + $taskOldNames.DirectoryName + ';' + (Join-Path $taskRoot '.tools\windows-sdk\c\um\x64') + ';' + (Join-Path $taskRoot '.tools\windows-sdk\c\ucrt\x64')
        $taskPublishOptions += '-p:IlcUseEnvironmentalTools=true'
    }
    & $taskDotnet publish src\LangSleeper\LangSleeper.csproj -c Release -r win-x64 -o artifacts\win-x64 @taskPublishOptions
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed.' }
    Copy-Item -LiteralPath LICENSE,LICENSE.ru.md -Destination artifacts\win-x64
    $taskBinary = Join-Path $taskRoot 'artifacts\win-x64\LangSleeper.exe'
    $taskHash = (Get-FileHash -LiteralPath $taskBinary -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts\win-x64\SHA256SUMS.txt') -Value ($taskHash + '  LangSleeper.exe') -Encoding Ascii
    Write-Output ('Ready: ' + $taskBinary)
}
finally { Pop-Location; $env:PATH = $taskPreviousPath; $env:LIB = $taskPreviousLib; $env:DOTNET_CLI_TELEMETRY_OPTOUT = $taskPreviousTelemetry }
