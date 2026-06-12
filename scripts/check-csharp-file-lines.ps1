param(
    [int] $WarnAt = 300,
    [int] $FailAt = 500
)

$ErrorActionPreference = "Stop"

function Resolve-RelativePath {
    param(
        [string] $Root,
        [string] $Path
    )

    $rootUri = New-Object Uri(($Root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar))
    $pathUri = New-Object Uri($Path)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$filePaths = & git -C $root ls-files --cached -- "*.cs"
if ($LASTEXITCODE -ne 0) {
    throw "Failed to list tracked C# files."
}

$files = $filePaths |
    Where-Object {
        $_ -notmatch "(^|/)(bin|obj)/|\.g\.cs$|\.Designer\.cs$"
    } |
    ForEach-Object {
        Get-Item -LiteralPath (Join-Path $root $_)
    }

$failed = $false
foreach ($file in $files) {
    $lineCount = (Get-Content -LiteralPath $file.FullName).Count
    $relative = Resolve-RelativePath -Root $root -Path $file.FullName

    if ($lineCount -gt $FailAt) {
        Write-Error "$relative has $lineCount lines, exceeding hard limit $FailAt."
        $failed = $true
    }
    elseif ($lineCount -gt $WarnAt) {
        Write-Warning "$relative has $lineCount lines, exceeding warning limit $WarnAt."
    }
}

if ($failed) {
    exit 1
}
