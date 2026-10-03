#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-NormalizedPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($full)
    if ($full.Length -gt $root.Length) { return $full.TrimEnd([char[]]@('\', '/')) }
    return $full
}

function Assert-ReadableFile($Item) {
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "リンクされた素材には対応していません: $($Item.Name)"
    }
    $stream = [IO.File]::Open($Item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try { $null = $stream.ReadByte() } finally { $stream.Dispose() }
}

try {
    if ($PWD.Provider.Name -ne 'FileSystem') { throw 'ファイルシステムのフォルダーへ移動してから実行してください。' }
    $workingDirectory = Get-NormalizedPath $PWD.ProviderPath
    $scriptDirectory = Get-NormalizedPath $PSScriptRoot
    if (-not [string]::Equals($workingDirectory, $scriptDirectory, [StringComparison]::OrdinalIgnoreCase)) {
        throw '現在位置とスクリプトの配置先が異なります。対象アーカイブへスクリプトを配置し、同じフォルダーへ移動して実行してください。'
    }
    $folder = Get-Item -LiteralPath $scriptDirectory -Force
    if (($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'リンクされたアーカイブには対応していません。' }
    $items = @(Get-ChildItem -LiteralPath $scriptDirectory -Force)
    if (@($items | Where-Object { $_.Name -ieq 'movie-maker-project.json' }).Count -gt 0) {
        throw 'movie-maker-project.json は既に存在します。上書きしません。'
    }
    $audioExtensions = @('.mp3', '.wav', '.m4a', '.flac', '.aac')
    $imageExtensions = @('.jpg', '.jpeg', '.png', '.webp')
    $audioItems = @($items | Where-Object { -not $_.PSIsContainer -and $audioExtensions -contains $_.Extension.ToLowerInvariant() })
    if ($audioItems.Count -eq 0) { throw '直下に対応する音声素材がありません。' }
    $numberedTracks = @()
    foreach ($item in $audioItems) {
        Assert-ReadableFile $item
        if ($item.Name -notmatch '^audio-([0-9]+)-(.+)$') { throw "番号付き音声名ではありません: $($item.Name)。順番を推測せず停止します。" }
        $numberText = $Matches[1]
        $originalName = $Matches[2]
        $number = 0
        if (-not [int]::TryParse($numberText, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
            throw "音声番号が不正です: $($item.Name)"
        }
        $numberedTracks += [pscustomobject]@{ Number = $number; AudioPath = $item.Name; OriginalFileName = $originalName }
    }
    $numberedTracks = @($numberedTracks | Sort-Object Number)
    for ($index = 0; $index -lt $numberedTracks.Count; $index++) {
        if ($numberedTracks[$index].Number -ne $index) { throw '音声番号に重複または欠番があります。0からの連番が必要です。' }
    }
    $images = @($items | Where-Object { -not $_.PSIsContainer -and $imageExtensions -contains $_.Extension.ToLowerInvariant() })
    if ($images.Count -ne 1 -or ($images[0].Name -notmatch '^image-.+\.(jpg|jpeg|png|webp)$' -and $images[0].Name -notmatch '^_draft_placeholder_.+\.png$')) {
        throw '現行命名の画像素材を一つに特定できません。'
    }
    Assert-ReadableFile $images[0]
    $layoutItems = @($items | Where-Object { $_.Name -ieq 'text-overlay-layout.json' })
    $layoutJson = $null
    if ($layoutItems.Count -gt 0) {
        if ($layoutItems.Count -ne 1 -or $layoutItems[0].PSIsContainer) { throw '文字合成レイアウトを一つに特定できません。' }
        Assert-ReadableFile $layoutItems[0]
        $layoutJson = [IO.File]::ReadAllText($layoutItems[0].FullName, [Text.UTF8Encoding]::new($false, $true))
        if ([string]::IsNullOrWhiteSpace($layoutJson)) { throw '文字合成レイアウトが空です。' }
        $null = ConvertFrom-Json -InputObject $layoutJson
    }
    $tracks = @($numberedTracks | ForEach-Object {
        [ordered]@{
            audioPath = $_.AudioPath; originalFileName = $_.OriginalFileName
            isNormalizationOverrideEnabled = $null; targetIntegratedLufs = $null; targetTruePeakDbtp = $null
        }
    })
    $project = [ordered]@{
        format = 'movie-maker-project'; schemaVersion = 1; origin = 'archive-scan'
        title = $null; useDraftMode = $null; profile = $null; orientation = $null; draftAudioQuality = $null
        imagePath = $images[0].Name; tracks = $tracks
        settings = [ordered]@{
            shortsMaximumSeconds = $null; oneMinuteShortsOffsetSeconds = $null; threeMinuteShortsOffsetSeconds = $null
            normalizationTargetIntegratedLufs = $null; normalizationTargetTruePeakDbtp = $null
            normalTextOverlayEnabled = $null; textOverlayLayoutJson = $layoutJson
        }
        appVersion = $null; preset = $null
    }
    $json = ConvertTo-Json -InputObject $project -Depth 20
    $destination = Join-Path $scriptDirectory 'movie-maker-project.json'
    $temporary = Join-Path $scriptDirectory ('.movie-maker-project-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $destination)
    }
    finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
    Write-Host "完了: movie-maker-project.json を作成しました（音声 $($tracks.Count) 曲）。"
    $numberedTracks | ForEach-Object { Write-Host ("  {0}: {1}" -f $_.Number, $_.OriginalFileName) }
    Write-Host '設定値は未確定です。アプリへJSONをドロップして今回の設定を確認・保存してください。'
}
catch {
    [Console]::Error.WriteLine("失敗: $($_.Exception.Message)")
    exit 1
}
