param(
    [string]$FilesMetaPath = "..\SmartSongSuggest\TaohSongSuggest\Configuration\InitialData\Files.meta",
    [string]$SongLibraryVersion,
    [string]$Top10kVersion,
    [switch]$TouchTop10kUpdated
)

$ErrorActionPreference = "Stop"

$resolvedPath = Resolve-Path -LiteralPath $FilesMetaPath
$meta = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json

if (![string]::IsNullOrEmpty($SongLibraryVersion)) {
    $meta.songLibraryVersion = $SongLibraryVersion
}

if (![string]::IsNullOrEmpty($Top10kVersion)) {
    $meta.top10kVersion = $Top10kVersion
}

if ($TouchTop10kUpdated) {
    $meta.top10kUpdated = [DateTime]::UtcNow.ToString("o")
}

$json = $meta | ConvertTo-Json -Compress
[System.IO.File]::WriteAllText($resolvedPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $resolvedPath"
