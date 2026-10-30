param([switch]$CheckLayout, [switch]$UpdateExample)

Set-StrictMode -Version Latest
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $projectRoot
try {
    function Invoke-Dotnet {
        param([string[]]$CommandArgs)
        & dotnet @CommandArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed with code $LASTEXITCODE" }
    }
    function Reset-GeneratedDirectory {
        param([string]$Path, [string]$AllowedRoot)
        $resolvedTarget = [IO.Path]::GetFullPath($Path)
        $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd('\') + '\'
        if (-not $resolvedTarget.StartsWith($resolvedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe generated path: $resolvedTarget" }
        if (Test-Path -LiteralPath $resolvedTarget) {
            if ((Get-Item -LiteralPath $resolvedTarget).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to clean a linked directory' }
            Remove-Item -LiteralPath $resolvedTarget -Recurse -Force -ErrorAction Stop
        }
        New-Item -ItemType Directory -Path $resolvedTarget -Force -ErrorAction Stop | Out-Null
    }
    $driverManifest = Get-Content 'drivers/ViGEmBus/manifest.json' -Raw -ErrorAction Stop | ConvertFrom-Json
    $driverFile = Join-Path 'drivers/ViGEmBus' $driverManifest.file
    if ((Get-FileHash -LiteralPath $driverFile -Algorithm SHA256 -ErrorAction Stop).Hash -ne $driverManifest.sha256) { throw 'Driver checksum mismatch' }
    $signature = Get-AuthenticodeSignature -LiteralPath $driverFile
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Nefarius Software Solutions*') { throw 'Driver publisher signature could not be verified' }

    Invoke-Dotnet @('restore','src/MyGamepads/MyGamepads.csproj','-r','win-x64','-p:PublishSingleFile=true','--locked-mode')
    Reset-GeneratedDirectory -Path (Join-Path $projectRoot 'artifacts/publish') -AllowedRoot (Join-Path $projectRoot 'artifacts')
    Invoke-Dotnet @('publish','src/MyGamepads/MyGamepads.csproj','-c','Release','-r','win-x64','--no-restore','-p:PublishProfile=Standalone','-o','artifacts/publish')
    $publishExe = Join-Path $projectRoot 'artifacts/publish/MyGamepads.exe'
    $validationDir = Join-Path $projectRoot 'artifacts/validation'
    New-Item -ItemType Directory -Path $validationDir -Force -ErrorAction Stop | Out-Null
    $checks = @('--self-test','--setup-test')
    if ($CheckLayout) { $checks += '--layout-check' }
    foreach ($check in $checks) {
        $checkProcess = Start-Process -FilePath $publishExe -ArgumentList @($check,'--output-dir',('"'+$validationDir+'"')) -Wait -PassThru -WindowStyle Hidden -ErrorAction Stop
        if ($checkProcess.ExitCode -ne 0) { throw "$check failed with code $($checkProcess.ExitCode); inspect artifacts/validation" }
    }

    [xml]$projectXml = Get-Content 'src/MyGamepads/MyGamepads.csproj' -Raw
    $version = [string]$projectXml.Project.PropertyGroup.Version
    $releaseDir = Join-Path $projectRoot "dist/MyGamepads-$version-win-x64"
    Reset-GeneratedDirectory -Path $releaseDir -AllowedRoot (Join-Path $projectRoot 'dist')
    Copy-Item -LiteralPath $publishExe -Destination (Join-Path $releaseDir 'MyGamepads.exe') -Force -ErrorAction Stop
    Copy-Item -LiteralPath 'README.md','AGENTS.md','LICENSE','THIRD-PARTY-NOTICES.txt' -Destination $releaseDir -Force -ErrorAction Stop
    Copy-Item -LiteralPath 'docs','licenses' -Destination $releaseDir -Recurse -Force -ErrorAction Stop
    $binaryZip = Join-Path $projectRoot "dist/MyGamepads-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $releaseDir '*') -DestinationPath $binaryZip -Force -ErrorAction Stop

    # Explicit allow-list: no logs, HID dumps, local paths or build caches in source release.
    $sourceStage = Join-Path $projectRoot 'artifacts/source'
    Reset-GeneratedDirectory -Path $sourceStage -AllowedRoot (Join-Path $projectRoot 'artifacts')
    $sourceFiles = @('README.md','AGENTS.md','LICENSE','THIRD-PARTY-NOTICES.txt','CHANGELOG.md','MyGamepads.slnx','Directory.Build.props','global.json','.gitignore','.editorconfig')
    Copy-Item -LiteralPath $sourceFiles -Destination $sourceStage -Force -ErrorAction Stop
    foreach ($folder in @('docs','drivers','scripts','licenses')) { Copy-Item -LiteralPath $folder -Destination $sourceStage -Recurse -Force -ErrorAction Stop }
    $sourceApp = Join-Path $sourceStage 'src/MyGamepads'
    New-Item -ItemType Directory -Path $sourceApp -Force -ErrorAction Stop | Out-Null
    Get-ChildItem -LiteralPath 'src/MyGamepads' -File | Where-Object { $_.Extension -in @('.cs','.csproj','.json') } | Copy-Item -Destination $sourceApp -Force -ErrorAction Stop
    Copy-Item -LiteralPath 'src/MyGamepads/Properties' -Destination $sourceApp -Recurse -Force -ErrorAction Stop
    Copy-Item -LiteralPath 'src/MyGamepads/Assets' -Destination $sourceApp -Recurse -Force -ErrorAction Stop
    $sourceZip = Join-Path $projectRoot "dist/MyGamepads-$version-source.zip"
    Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceZip -Force -ErrorAction Stop
    $hashLines = foreach ($file in @($binaryZip,$sourceZip,(Join-Path $releaseDir 'MyGamepads.exe'))) {
        $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(),[IO.Path]::GetRelativePath((Join-Path $projectRoot 'dist'),$file).Replace('\','/')
    }
    Set-Content -LiteralPath 'dist/SHA256SUMS.txt' -Value $hashLines -Encoding utf8 -ErrorAction Stop
    if ($UpdateExample) { & (Join-Path $PSScriptRoot 'Update-Example.ps1') -Archive $binaryZip -Version $version }
    Write-Output "Release ready: $binaryZip"
    Write-Output "Source ready: $sourceZip"
}
finally { Pop-Location }
