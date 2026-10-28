param(
    [string[]]$Runtimes = @("win-x64", "linux-x64"),
    [switch]$NoZapret,
    [switch]$NoPackage
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$root = $PSScriptRoot
$project = Join-Path $root "src\ZapretGui\ZapretGui.csproj"
$dist = Join-Path $root "dist"
$release = Join-Path $dist "release"
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

foreach ($rid in $Runtimes) {
    Write-Host "==> publish $rid" -ForegroundColor Cyan
    dotnet publish $project -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -o (Join-Path $dist "build\$rid")
    if ($LASTEXITCODE -ne 0) { throw "Сборка $rid завершилась с ошибкой" }
}

if ($NoPackage) { Write-Host "Готово: $dist" -ForegroundColor Green; return }

$zapretDir = $null
if (-not $NoZapret) {
    Write-Host "==> загрузка zapret-discord-youtube" -ForegroundColor Cyan
    $rel = Invoke-RestMethod "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest" -Headers @{ "User-Agent" = "ZapretGUI-build" }
    $asset = $rel.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
    $tmp = Join-Path $env:TEMP ("zapret-gui-build-" + [guid]::NewGuid())
    New-Item -ItemType Directory $tmp | Out-Null
    $zip = Join-Path $tmp $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    Expand-Archive $zip (Join-Path $tmp "x")
    $zapretDir = Get-ChildItem (Join-Path $tmp "x") -Recurse -Directory -Filter bin | Select-Object -First 1 | ForEach-Object { $_.Parent.FullName }
    Write-Host "zapret-discord-youtube $($rel.tag_name)"
}

New-Item -ItemType Directory $release -Force | Out-Null
Get-ChildItem $release | Remove-Item -Recurse -Force
$docs = @((Join-Path $root "README.md"), (Join-Path $root "LICENSE"), (Join-Path $root "CHANGELOG.md"))

function Escape-Mtree([string]$s) {
    $sb = New-Object Text.StringBuilder
    foreach ($b in [Text.Encoding]::UTF8.GetBytes($s.Replace('\', '/'))) {
        if ($b -le 32 -or $b -ge 127 -or $b -eq 35 -or $b -eq 92) { [void]$sb.Append('\' + [Convert]::ToString($b, 8).PadLeft(3, '0')) }
        else { [void]$sb.Append([char]$b) }
    }
    $sb.ToString()
}

foreach ($rid in $Runtimes) {
    $name = "ZapretGUI-$version-$rid"
    $stage = Join-Path $dist "stage\$name"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory $stage | Out-Null
    Copy-Item (Join-Path $dist "build\$rid\*") $stage -Recurse
    Copy-Item $docs $stage
    if ($zapretDir) { Copy-Item $zapretDir (Join-Path $stage "zapret") -Recurse }

    if ($rid -like "win-*") {
        Write-Host "==> $name.zip" -ForegroundColor Cyan
        $fs = [IO.File]::Open((Join-Path $release "$name.zip"), [IO.FileMode]::Create)
        $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
        foreach ($file in Get-ChildItem $stage -Recurse -File) {
            $entry = $name + "/" + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $file.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
        $za.Dispose()
        $fs.Dispose()
    }
    else {
        Copy-Item (Join-Path $root "packaging\linux\*") $stage
        Copy-Item (Join-Path $root "src\ZapretGui\Assets\app.png") (Join-Path $stage "zapret-gui.png")
        Write-Host "==> $name.tar.gz" -ForegroundColor Cyan
        $executables = @("ZapretGUI", "install.sh", "uninstall.sh")
        $lines = @("#mtree", "$(Escape-Mtree $name) type=dir mode=0755")
        foreach ($item in Get-ChildItem $stage -Recurse | Sort-Object FullName) {
            $rel = $name + "/" + $item.FullName.Substring($stage.Length + 1)
            if ($item.PSIsContainer) { $lines += "$(Escape-Mtree $rel) type=dir mode=0755" }
            else {
                $mode = if ($executables -contains $item.Name -and $item.DirectoryName -eq $stage) { "0755" } else { "0644" }
                $lines += "$(Escape-Mtree $rel) type=file mode=$mode contents=$(Escape-Mtree $item.FullName)"
            }
        }
        $mtree = Join-Path $dist "stage\$name.mtree"
        [IO.File]::WriteAllLines($mtree, $lines, (New-Object Text.UTF8Encoding $false))
        & tar.exe -czf (Join-Path $release "$name.tar.gz") --uname root --gname root "@$mtree"
        if ($LASTEXITCODE -ne 0) { throw "Не удалось создать $name.tar.gz" }
    }
}

Remove-Item (Join-Path $dist "stage") -Recurse -Force
if ($tmp) { Remove-Item $tmp -Recurse -Force }

$sums = Get-ChildItem $release -File | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
[IO.File]::WriteAllLines((Join-Path $release "SHA256SUMS.txt"), $sums)

Write-Host "Готово: $release" -ForegroundColor Green
Get-ChildItem $release | ForEach-Object { "  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
