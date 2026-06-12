[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$HistoryDirectory,

    [string]$RunNumber = $env:GITHUB_RUN_NUMBER,

    [string]$CommitSha = $env:GITHUB_SHA,

    [string]$Branch = $env:GITHUB_REF_NAME,

    [string]$Repository = $env:GITHUB_REPOSITORY,

    [int]$TrendLimit = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Convert-ToDouble {
    param([object]$Value)

    if ($null -eq $Value -or $Value -eq '') {
        return 0.0
    }

    if ($Value -isnot [string]) {
        return [System.Convert]::ToDouble($Value, $InvariantCulture)
    }

    $text = [string]$Value
    if ($text.Contains(',') -and -not $text.Contains('.')) {
        return [double]::Parse($text.Replace(',', '.'), $InvariantCulture)
    }

    return [double]::Parse($text, $InvariantCulture)
}

function Format-Number {
    param(
        [string]$Format,
        [object[]]$Arguments
    )

    return [string]::Format($InvariantCulture, $Format, $Arguments)
}

function Format-Nanoseconds {
    param([double]$Nanoseconds)

    if ($Nanoseconds -ge 1000000) {
        return Format-Number '{0:n2} ms' @($Nanoseconds / 1000000)
    }

    if ($Nanoseconds -ge 1000) {
        return Format-Number '{0:n2} us' @($Nanoseconds / 1000)
    }

    return Format-Number '{0:n2} ns' @($Nanoseconds)
}

function Format-Bytes {
    param([double]$Bytes)

    if ($Bytes -le 0) {
        return '0 B'
    }

    if ($Bytes -ge 1024) {
        return Format-Number '{0:n2} KB' @($Bytes / 1024)
    }

    return Format-Number '{0:n0} B' @($Bytes)
}

function Format-RawNumber {
    param([double]$Value)

    return Format-Number '{0:0.####}' @($Value)
}

function Get-ShortSha {
    param([string]$Sha)

    if ([string]::IsNullOrWhiteSpace($Sha)) {
        return ''
    }

    return $Sha.Substring(0, [Math]::Min(12, $Sha.Length))
}

function Get-BenchmarkName {
    param([object]$Benchmark)

    $name = "$($Benchmark.Type).$($Benchmark.Method)"
    if (-not [string]::IsNullOrWhiteSpace([string]$Benchmark.Parameters)) {
        $name += " [$($Benchmark.Parameters)]"
    }

    return $name
}

function Get-RunKey {
    param([object]$Record)

    return "$($Record.RunNumber)|$($Record.Commit)"
}

function Get-RunSortValue {
    param([object]$Record)

    $parsed = 0
    if ([int]::TryParse([string]$Record.RunNumber, [ref]$parsed)) {
        return $parsed
    }

    return [int]::MaxValue
}

function Read-BenchmarkReports {
    param([string]$Directory)

    $jsonFiles = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter '*-report-full-compressed.json')
    if ($jsonFiles.Count -eq 0) {
        throw "No BenchmarkDotNet JSON reports found under '$Directory'."
    }

    $timestamp = [DateTimeOffset]::UtcNow.ToString('o', $InvariantCulture)
    $shortSha = Get-ShortSha $CommitSha
    $records = foreach ($jsonFile in $jsonFiles) {
        $summary = Get-Content -LiteralPath $jsonFile.FullName -Raw | ConvertFrom-Json
        foreach ($benchmark in @($summary.Benchmarks)) {
            [pscustomobject]@{
                RunNumber      = $RunNumber
                TimestampUtc   = $timestamp
                Repository     = $Repository
                Branch         = $Branch
                Commit         = $shortSha
                Runtime        = [string]$summary.HostEnvironmentInfo.RuntimeVersion
                Os             = [string]$summary.HostEnvironmentInfo.OsVersion
                Benchmark      = Get-BenchmarkName $benchmark
                Type           = [string]$benchmark.Type
                Method         = [string]$benchmark.Method
                Parameters     = [string]$benchmark.Parameters
                MeanNs         = Format-RawNumber (Convert-ToDouble $benchmark.Statistics.Mean)
                MedianNs       = Format-RawNumber (Convert-ToDouble $benchmark.Statistics.Median)
                AllocatedBytes = Format-RawNumber (Convert-ToDouble $benchmark.Memory.BytesAllocatedPerOperation)
            }
        }
    }

    return @($records | Sort-Object Type, Method, Parameters)
}

function Read-History {
    param([string]$Directory)

    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    return @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter 'benchmark-results.csv' |
        ForEach-Object { Import-Csv -LiteralPath $_.FullName })
}

function Merge-History {
    param([object[]]$Records)

    $deduped = [ordered]@{}
    foreach ($record in $Records) {
        $deduped["$(Get-RunKey $record)|$($record.Benchmark)"] = $record
    }

    $runs = @($deduped.Values |
        Group-Object { Get-RunKey $_ } |
        Sort-Object { Get-RunSortValue $_.Group[0] }, { [string]$_.Group[0].TimestampUtc } |
        Select-Object -Last $TrendLimit)
    $selectedRunKeys = @($runs | ForEach-Object { $_.Name })

    return @($deduped.Values | Where-Object { $selectedRunKeys -contains (Get-RunKey $_) })
}

function Select-TrendBenchmark {
    param([object[]]$Records)

    $preferred = @(
        'PeerRoundTripBenchmarks.MovePlayerAsync [EndToEndLowAllocationProfile=True]',
        'PeerRoundTripBenchmarks.MovePlayerAsync [EndToEndLowAllocationProfile=False]',
        'FramingBenchmarks.FrameRequest',
        'ZeroAllocUserFlowBenchmarks.FullGameplaySessionFlow'
    )

    foreach ($name in $preferred) {
        if ($Records | Where-Object { [string]$_.Benchmark -eq $name } | Select-Object -First 1) {
            return $name
        }
    }

    return ($Records | Sort-Object Benchmark | Select-Object -First 1).Benchmark
}

function New-MermaidTrend {
    param([object[]]$Records)

    $runGroups = @($Records |
        Group-Object { Get-RunKey $_ } |
        Sort-Object { Get-RunSortValue $_.Group[0] }, { [string]$_.Group[0].TimestampUtc })
    if ($runGroups.Count -eq 0) {
        return ''
    }

    $benchmark = Select-TrendBenchmark $Records
    if ([string]::IsNullOrWhiteSpace($benchmark)) {
        return ''
    }

    $means = @(foreach ($run in $runGroups) {
        $record = $run.Group |
            Where-Object { [string]$_.Benchmark -eq $benchmark } |
            Select-Object -First 1
        if ($null -eq $record) {
            0
        }
        else {
            Convert-ToDouble $record.MeanNs
        }
    })

    $baseline = @($means | Where-Object { $_ -gt 0 }) | Select-Object -First 1
    if ($null -eq $baseline) {
        return ''
    }

    $relative = @($means | ForEach-Object {
        if ($_ -le 0) { 0 } else { [Math]::Round(($_ / $baseline) * 100, 2) }
    })
    $labels = @($runGroups | ForEach-Object { '"' + '#' + $_.Group[0].RunNumber + '"' })
    $min = [Math]::Max(0, [Math]::Floor((($relative | Measure-Object -Minimum).Minimum) / 5) * 5)
    $max = [Math]::Max(105, [Math]::Ceiling((($relative | Measure-Object -Maximum).Maximum) / 5) * 5)
    $title = ([string]$benchmark) -replace '"', "'"
    $relativeText = @($relative | ForEach-Object {
        [string]::Format($InvariantCulture, '{0:0.##}', $_)
    })

    return @(
        '```mermaid'
        'xychart-beta'
        "    title ""$title mean trend (relative, lower is better)"""
        "    x-axis [$($labels -join ', ')]"
        "    y-axis ""Mean %"" $min --> $max"
        "    line [$($relativeText -join ', ')]"
        '```'
    ) -join [Environment]::NewLine
}

function Escape-MarkdownCell {
    param([string]$Value)

    return $Value -replace '\|', '\|'
}

function Write-Summary {
    param(
        [object[]]$Current,
        [object[]]$History,
        [string]$Path
    )

    $runCount = @($History | Group-Object { Get-RunKey $_ }).Count
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('## BenchmarkDotNet results')
    $lines.Add('')
    $lines.Add("Run: $RunNumber  ")
    $lines.Add("Branch: ``$Branch``  ")
    $lines.Add("Commit: ``$(Get-ShortSha $CommitSha)``")
    $lines.Add('')
    $lines.Add('This is a short CI benchmark smoke run. Use the trend and raw artifacts for direction, not as a release-grade measurement on a dedicated machine.')
    $lines.Add('')
    $lines.Add('| Benchmark | Mean | Allocated |')
    $lines.Add('| --- | ---: | ---: |')

    foreach ($record in $Current) {
        $name = Escape-MarkdownCell $record.Benchmark
        $mean = Format-Nanoseconds (Convert-ToDouble $record.MeanNs)
        $allocated = Format-Bytes (Convert-ToDouble $record.AllocatedBytes)
        $lines.Add("| $name | $mean | $allocated |")
    }

    $lines.Add('')
    if ($runCount -gt 1) {
        $lines.Add("Trend history includes the latest $runCount artifacted CI runs available to this workflow.")
    }
    else {
        $lines.Add('No previous benchmark artifact was found for this branch yet; the trend becomes useful after later CI runs.')
    }

    $trend = New-MermaidTrend $History
    if (-not [string]::IsNullOrWhiteSpace($trend)) {
        $lines.Add('')
        $lines.Add($trend)
    }

    $lines.Add('')
    $lines.Add('Uploaded artifact `benchmark-results` contains raw BenchmarkDotNet reports, `benchmark-results.csv`, `benchmark-history.csv`, and this summary.')
    Set-Content -LiteralPath $Path -Value $lines -Encoding utf8
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$current = Read-BenchmarkReports $ResultsDirectory
$current | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'benchmark-results.csv') -NoTypeInformation

$history = Merge-History (@(Read-History $HistoryDirectory) + @($current))
$history |
    Sort-Object { Get-RunSortValue $_ }, Benchmark |
    Export-Csv -LiteralPath (Join-Path $OutputDirectory 'benchmark-history.csv') -NoTypeInformation

Write-Summary `
    -Current $current `
    -History $history `
    -Path (Join-Path $OutputDirectory 'benchmark-summary.md')
