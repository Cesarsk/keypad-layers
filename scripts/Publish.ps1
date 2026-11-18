param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $sourceRoot 'out\win-x64' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory already exists. Choose a new output directory to preserve previous builds.' }
Push-Location $sourceRoot
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
    $isccPath = if ($iscc) { $iscc.Source } else { 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $isccPath)) { throw 'Inno Setup 6 compiler (ISCC.exe) is required to build the installer.' }
    [xml]$project = Get-Content -LiteralPath 'KeypadLayers.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    & $isccPath "/DAppVersion=$version" (Join-Path $sourceRoot 'installer\KeypadLayers.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    $installer = Join-Path $sourceRoot "out\installer\KeypadLayersSetup-$version.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output was not created.' }
    Copy-Item -LiteralPath $installer -Destination $OutputDirectory
    $smokeRoot = Join-Path $sourceRoot 'out\installer-smoke'
    if (Test-Path -LiteralPath $smokeRoot) { throw 'Installer smoke-test directory already exists; refusing to overwrite it.' }
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    try {
        & $isccPath "/DAppVersion=$version" '/DIsolatedSmokeBuild' "/O$smokeRoot" '/FKeypadLayersSetup-Smoke' (Join-Path $sourceRoot 'installer\KeypadLayers.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Isolated installer smoke build failed.' }
        $smokeSetup = Join-Path $smokeRoot 'KeypadLayersSetup-Smoke.exe'
        $smokeInstall = Join-Path $smokeRoot 'installed'
        $installArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$smokeInstall`"")
        $installProcess = Start-Process -FilePath $smokeSetup -ArgumentList $installArgs -Wait -PassThru -WindowStyle Hidden
        if ($installProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $smokeInstall 'KeypadLayers.exe'))) { throw 'Silent installer smoke test failed.' }
        $uninstaller = Get-ChildItem -LiteralPath $smokeInstall -Filter 'unins*.exe' | Select-Object -First 1
        if (-not $uninstaller) { throw 'Installer did not create an uninstaller.' }
        $uninstallProcess = Start-Process -FilePath $uninstaller.FullName -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru -WindowStyle Hidden
        if ($uninstallProcess.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $smokeInstall 'KeypadLayers.exe'))) { throw 'Silent uninstaller smoke test failed.' }
    } finally {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
    $verification = Join-Path $OutputDirectory 'verification'
    New-Item -ItemType Directory -Path $verification | Out-Null
    $test = Start-Process -FilePath (Join-Path $OutputDirectory 'KeypadLayers.exe') -ArgumentList '--self-test',('"' + $verification + '"') -Wait -PassThru -WindowStyle Hidden
    Get-Content -LiteralPath (Join-Path $verification 'self-test.txt')
    if ($test.ExitCode -ne 0) { throw 'Self-test failed.' }
    $revision = 'working-tree'
    if ((Test-Path -LiteralPath (Join-Path $sourceRoot '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        try { $candidate = & git rev-parse HEAD 2>$null; if ($LASTEXITCODE -eq 0) { $revision = $candidate } } catch { $revision = 'working-tree' }
    }
    [ordered]@{
        project = 'keypad-layers'; version = [string]$project.Project.PropertyGroup.Version
        source_revision = $revision; build_timestamp = [DateTime]::UtcNow.ToString('o')
        source_path = $sourceRoot; build_machine = $env:COMPUTERNAME
        verification_commands = @('dotnet build -c Release', 'dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true', 'iscc /DAppVersion=<version> installer\KeypadLayers.iss', 'silent installer install and uninstall smoke test', 'KeypadLayers.exe --self-test verification')
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-info.json') -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'README.md') -Destination $OutputDirectory
    Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne 'checksums.txt' } | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    } | Set-Content -LiteralPath (Join-Path $OutputDirectory 'checksums.txt') -Encoding ascii
    Write-Output "Portable app: $(Join-Path $OutputDirectory 'KeypadLayers.exe')"
    Write-Output "Installer: $installer"
} finally { Pop-Location }
