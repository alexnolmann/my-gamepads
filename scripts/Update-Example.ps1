param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$Version
)

Set-StrictMode -Version Latest
$exampleRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../releases'))
$archivePath = (Resolve-Path -LiteralPath $Archive -ErrorAction Stop).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveCheck = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($requiredEntry in @('MyGamepads.exe','README.md','AGENTS.md','LICENSE','THIRD-PARTY-NOTICES.txt')) {
        if ($null -eq $archiveCheck.GetEntry($requiredEntry)) { throw "Incomplete example archive: $requiredEntry is missing" }
    }
} finally { $archiveCheck.Dispose() }
New-Item -ItemType Directory -Path $exampleRoot -Force -ErrorAction Stop | Out-Null
$exampleZip = Join-Path $exampleRoot 'MyGamepads-win-x64.zip'
if (-not $archivePath.Equals($exampleZip,[StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $archivePath -Destination $exampleZip -Force -ErrorAction Stop
}
$exampleHash = (Get-FileHash -LiteralPath $exampleZip -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $exampleRoot 'SHA256SUMS.txt') -Value ($exampleHash+'  MyGamepads-win-x64.zip') -Encoding utf8 -ErrorAction Stop
$exampleReadme = [string]::Join([Environment]::NewLine,@(
    '# Одна текущая примерная сборка',
    '',
    'Версия приложения: **{{VERSION}}**. Платформа: Windows 10/11 x64.',
    '',
    '[Скачать ZIP](MyGamepads-win-x64.zip) · [SHA-256](SHA256SUMS.txt) · [Инструкция для агента](../AGENTS.md)',
    '',
    'Это готовый автономный EXE с .NET и оригинальным установщиком ViGEmBus внутри. SDK и сборка исходников для запуска не нужны. Распакуйте архив, откройте MyGamepads.exe и следуйте README внутри. Приложение не подписано; подпись Nefarius относится к встроенному установщику драйвера.',
    '',
    'Пример проверен на исходных двух геймпадах Wireless Controller (VID 054C; PID 05C4 / 09CC), в том числе одновременно по Bluetooth в игре. Другим моделям может понадобиться адаптация. Агенту: не считайте наличие ZIP доказательством поддержки оборудования нового пользователя.',
    '',
    'Контрольная сумма архива:',
    '',
    '```text',
    '{{SHA256}}  MyGamepads-win-x64.zip',
    '```',
    '',
    'Каталог содержит только одну текущую сборку с постоянным именем файла. Чтобы обновить её из исходников, выполните из корня репозитория:',
    '',
    '```powershell',
    'pwsh -File scripts/build.ps1 -UpdateExample',
    '```',
    '',
    'Или обновите пример из уже подготовленного полного релизного ZIP:',
    '',
    '```powershell',
    'pwsh -File scripts/Update-Example.ps1 -Archive .\dist\MyGamepads-{{VERSION}}-win-x64.zip -Version {{VERSION}}',
    '```',
    '',
    'Сценарий обновляет ZIP, эту страницу и SHA-256. Проверьте git diff и явно добавьте нужные файлы в коммит. Не накапливайте старые бинарные архивы в этом каталоге. Это примерная сборка в Git, а не автоматическая публикация GitHub Release.'
))
$exampleReadme = $exampleReadme.Replace('{{VERSION}}',$Version).Replace('{{SHA256}}',$exampleHash)
Set-Content -LiteralPath (Join-Path $exampleRoot 'README.md') -Value $exampleReadme -Encoding utf8 -ErrorAction Stop
Write-Output "Updated example: $exampleZip"
