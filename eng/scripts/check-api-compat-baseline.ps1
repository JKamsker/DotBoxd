param(
    [string] $BaselineDirectory = "docs/api-baselines",
    [switch] $Update
)

$ErrorActionPreference = "Stop"

# Scripts live under eng/scripts/ (two levels below repo root); walk up twice.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$baselineRoot = if ([System.IO.Path]::IsPathRooted($BaselineDirectory)) {
    $BaselineDirectory
} else {
    Join-Path $root $BaselineDirectory
}

$packages = @(
    @{ Id = "DotBoxD.Kernels"; Path = "src/Kernels/DotBoxD.Kernels" },
    @{ Id = "DotBoxD.Kernels.Validation"; Path = "src/Kernels/DotBoxD.Kernels.Validation" },
    @{ Id = "DotBoxD.Kernels.Runtime"; Path = "src/Kernels/DotBoxD.Kernels.Runtime" },
    @{ Id = "DotBoxD.Kernels.Serialization.Json"; Path = "src/Kernels/DotBoxD.Kernels.Serialization.Json" },
    @{ Id = "DotBoxD.Hosting.Http"; Path = "src/Hosting/DotBoxD.Hosting.Http" },
    @{ Id = "DotBoxD.Pushdown.Services"; Path = "src/Pushdown/DotBoxD.Pushdown.Services" },
    @{ Id = "DotBoxD.Services"; Path = "src/Services/DotBoxD.Services" },
    @{ Id = "DotBoxD.Codecs.MessagePack"; Path = "src/Channels/DotBoxD.Codecs.MessagePack" },
    @{ Id = "DotBoxD.Transports.NamedPipes"; Path = "src/Channels/DotBoxD.Transports.NamedPipes" },
    @{ Id = "DotBoxD.Transports.Tcp"; Path = "src/Channels/DotBoxD.Transports.Tcp" },
    @{ Id = "DotBoxD.Kernels.Interpreter"; Path = "src/Kernels/DotBoxD.Kernels.Interpreter" },
    @{ Id = "DotBoxD.Kernels.Compiler"; Path = "src/Kernels/DotBoxD.Kernels.Compiler" },
    @{ Id = "DotBoxD.Kernels.Verifier"; Path = "src/Kernels/DotBoxD.Kernels.Verifier" },
    @{ Id = "DotBoxD.Hosting"; Path = "src/Hosting/DotBoxD.Hosting" },
    @{ Id = "DotBoxD.Plugins.Analyzer"; Path = "src/CodeGeneration/DotBoxD.Plugins.Analyzer" },
    @{ Id = "DotBoxD.Plugins"; Path = "src/Hosting/DotBoxD.Plugins" },
    @{ Id = "DotBoxD.Abstractions"; Path = "src/Hosting/DotBoxD.Abstractions" },
    @{ Id = "DotBoxD"; Path = "src/Meta/DotBoxD" },
    @{ Id = "DotBoxD.Services.All"; Path = "src/Meta/DotBoxD.Services.All" }
)

$publicSurfaceAccessibilityPattern = "^(public|protected\s+internal|internal\s+protected|protected)\b"

function Normalize-ApiLine([string] $Line) {
    $trimmed = Remove-LineComment $Line
    $trimmed = $trimmed.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed) -or
        $trimmed.StartsWith("//", [StringComparison]::Ordinal) -or
        $trimmed.StartsWith("[", [StringComparison]::Ordinal)) {
        return $null
    }

    if ($trimmed -notmatch $publicSurfaceAccessibilityPattern) {
        return $null
    }

    if ($trimmed -match "^(public|protected)\s+(get|set|init)\b") {
        return $null
    }

    $normalized = $trimmed -replace "\s+", " "
    $normalized = $normalized.TrimEnd("{", ";").Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    return $normalized
}

function Remove-LineComment([string] $Line) {
    $doubleQuote = [char]34
    $singleQuote = [char]39
    $backslash = [char]92
    $slash = [char]47
    $at = [char]64
    $nullChar = [char]0

    $inString = $false
    $inVerbatimString = $false
    $inRawString = $false
    $rawStringQuotes = 0
    $inChar = $false

    for ($index = 0; $index -lt $Line.Length; $index++) {
        $current = $Line[$index]
        $next = if ($index + 1 -lt $Line.Length) { $Line[$index + 1] } else { $nullChar }

        if ($inRawString) {
            if ($current -eq $doubleQuote) {
                $quoteCount = Count-ConsecutiveChars $Line $index $doubleQuote
                if ($quoteCount -ge $rawStringQuotes) {
                    $index += $rawStringQuotes - 1
                    $inRawString = $false
                }
            }

            continue
        }

        if ($inString) {
            if ($inVerbatimString) {
                if ($current -eq $doubleQuote -and $next -eq $doubleQuote) {
                    $index++
                } elseif ($current -eq $doubleQuote) {
                    $inString = $false
                    $inVerbatimString = $false
                }

                continue
            }

            if ($current -eq $backslash) {
                $index++
            } elseif ($current -eq $doubleQuote) {
                $inString = $false
            }

            continue
        }

        if ($inChar) {
            if ($current -eq $backslash) {
                $index++
            } elseif ($current -eq $singleQuote) {
                $inChar = $false
            }

            continue
        }

        if ($current -eq $slash -and $next -eq $slash) {
            return $Line.Substring(0, $index)
        }

        if ($current -eq $at -and $next -eq $doubleQuote) {
            $inString = $true
            $inVerbatimString = $true
            $index++
            continue
        }

        if ($current -eq $doubleQuote) {
            $quoteCount = Count-ConsecutiveChars $Line $index $doubleQuote
            if ($quoteCount -ge 3) {
                $inRawString = $true
                $rawStringQuotes = $quoteCount
                $index += $quoteCount - 1
            } else {
                $inString = $true
                $inVerbatimString = $false
            }

            continue
        }

        if ($current -eq $singleQuote) {
            $inChar = $true
        }
    }

    return $Line
}

function Count-ConsecutiveChars([string] $Text, [int] $StartIndex, [char] $Character) {
    $count = 0
    for ($index = $StartIndex; $index -lt $Text.Length; $index++) {
        if ($Text[$index] -ne $Character) {
            break
        }

        $count++
    }

    return $count
}

function Get-ParenthesisDelta([string] $Text) {
    $delta = 0
    foreach ($character in $Text.ToCharArray()) {
        if ($character -eq "(") {
            $delta++
        } elseif ($character -eq ")") {
            $delta--
        }
    }

    return $delta
}

function Test-TypeDeclaration([string] $Trimmed) {
    # Matches class/struct/record/interface/enum declarations regardless of the
    # leading access modifier so we can track the accessibility of containing types.
    return $Trimmed -match "\b(class|struct|interface|enum|record(\s+(class|struct))?)\s+[A-Za-z_@]"
}

function Test-TypeDeclarationPublic([string] $Trimmed) {
    # A type contributes to the effective public surface only when its own
    # declaration is public, protected, protected internal, or internal protected.
    if ($Trimmed -notmatch $publicSurfaceAccessibilityPattern) {
        return $false
    }

    # protected internal and internal protected are public-surface-visible; a bare
    # internal/private/file modifier is not.
    if (($Trimmed -match "^internal\b" -and $Trimmed -notmatch "^internal\s+protected\b") -or
        $Trimmed -match "^private\b" -or
        $Trimmed -match "^file\b") {
        return $false
    }

    return $true
}

# Computes, for each source line, whether the innermost enclosing type scope is
# effectively public. A line declared inside an internal/private/file type (or a
# public type nested inside such a type) is reported as not effectively public, so
# its members are excluded from the public API surface. Top-level (namespace) lines
# have no enclosing type and are reported as public; their own access modifier still
# gates whether they are recorded. Fails closed: an unrecognized type modifier is
# treated as non-public.
function Get-ContainingTypePublicFlags([string[]] $Lines) {
    $flags = New-Object "System.Collections.Generic.List[bool]"

    # Stack of effective-public flags, one entry per open brace scope. A scope value
    # is the effective public-ness of the nearest enclosing type at that depth.
    $scopeStack = New-Object "System.Collections.Generic.List[bool]"

    # A type declaration and its opening brace can span multiple lines (for example a
    # base list before the brace). $pendingTypePublic holds the effective public-ness of
    # such a declaration until its opening brace is consumed.
    $hasPendingType = $false
    $pendingTypePublic = $true

    foreach ($line in $Lines) {
        $trimmed = (Remove-LineComment $line).Trim()

        # The flag for this line reflects the type scope in effect at the start of the
        # line, before this line opens or closes any braces. A type declaration line is
        # therefore evaluated against its parent scope, while members appear after the
        # type's opening brace and see the pushed type scope.
        $containingPublic = if ($scopeStack.Count -gt 0) {
            $scopeStack[$scopeStack.Count - 1]
        } else {
            $true
        }

        $flags.Add($containingPublic)

        if ([string]::IsNullOrWhiteSpace($trimmed) -or
            $trimmed.StartsWith("[", [StringComparison]::Ordinal)) {
            continue
        }

        # Determine whether this line introduces a type scope and, if so, its effective
        # public-ness relative to the current containing type scope. A pending type from
        # an earlier line takes precedence until its brace is found.
        if (-not $hasPendingType -and (Test-TypeDeclaration $trimmed)) {
            $hasPendingType = $true
            $pendingTypePublic = (Test-TypeDeclarationPublic $trimmed) -and $containingPublic
        }

        foreach ($character in $trimmed.ToCharArray()) {
            if ($character -eq "{") {
                if ($hasPendingType) {
                    # The opening brace belongs to the pending type declaration.
                    [void] $scopeStack.Add($pendingTypePublic)
                    $hasPendingType = $false
                } else {
                    # Non-type block (method body, accessor, initializer): inherit the
                    # current containing type scope so nested members stay gated correctly.
                    $inherited = if ($scopeStack.Count -gt 0) {
                        $scopeStack[$scopeStack.Count - 1]
                    } else {
                        $true
                    }

                    [void] $scopeStack.Add($inherited)
                }
            } elseif ($character -eq "}") {
                if ($scopeStack.Count -gt 0) {
                    $scopeStack.RemoveAt($scopeStack.Count - 1)
                }
            } elseif ($character -eq ";" -and $hasPendingType) {
                # A positional record without a body (for example
                # `public record Foo(int X);`) terminates with a semicolon and opens no
                # type scope. Clear the pending state so a later unrelated brace is not
                # mistaken for this type's body.
                $hasPendingType = $false
            }
        }
    }

    return $flags
}

function Normalize-ApiDeclaration([string] $Declaration) {
    $lines = New-Object "System.Collections.Generic.List[string]"
    foreach ($line in ($Declaration -split "\r?\n")) {
        $trimmed = (Remove-LineComment $line).Trim()
        if (-not [string]::IsNullOrWhiteSpace($trimmed) -and
            -not $trimmed.StartsWith("[", [StringComparison]::Ordinal)) {
            $lines.Add($trimmed)
        }
    }

    if ($lines.Count -eq 0) {
        return $null
    }

    $normalized = ($lines -join " ") -replace "\s+", " "
    $normalized = $normalized.TrimEnd("{", ";").Trim()
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized -notmatch $publicSurfaceAccessibilityPattern) {
        return $null
    }

    if ($normalized -match "^(public|protected)\s+(get|set|init)\b") {
        return $null
    }

    return $normalized
}

function Add-GenericConstraints([string] $Declaration, [string[]] $Lines, [int] $StartIndex, [ref] $Index) {
    $result = $Declaration
    for ($constraintIndex = $StartIndex; $constraintIndex -lt $Lines.Count; $constraintIndex++) {
        $trimmed = (Remove-LineComment $Lines[$constraintIndex]).Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or
            $trimmed.StartsWith("[", [StringComparison]::Ordinal) -or
            $trimmed -notmatch "^where\s+") {
            break
        }

        $constraint = Normalize-GenericConstraintLine $trimmed
        if (-not [string]::IsNullOrWhiteSpace($constraint)) {
            $result = "$result`n$constraint"
            $Index.Value = $constraintIndex
        }
    }

    return $result
}

function Normalize-GenericConstraintLine([string] $Line) {
    $constraint = $Line
    $arrow = $constraint.IndexOf("=>", [StringComparison]::Ordinal)
    if ($arrow -ge 0) {
        $constraint = $constraint.Substring(0, $arrow)
    }

    $brace = $constraint.IndexOf("{", [StringComparison]::Ordinal)
    if ($brace -ge 0) {
        $constraint = $constraint.Substring(0, $brace)
    }

    return $constraint.TrimEnd(";").Trim()
}

function Add-EnumMembers([string[]] $Lines, [System.Collections.Generic.HashSet[string]] $Api, [System.Collections.Generic.List[bool]] $ContainingTypePublic) {
    $enumName = $null
    $pendingEnumName = $null
    $braceDepth = 0

    for ($index = 0; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        $trimmed = (Remove-LineComment $line).Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or
            $trimmed.StartsWith("[", [StringComparison]::Ordinal)) {
            continue
        }

        if ($null -eq $enumName) {
            # Only treat this as a public enum when its containing type is effectively
            # public; a public enum nested in an internal type is not consumer-visible.
            if ($null -eq $pendingEnumName -and
                $ContainingTypePublic[$index] -and
                $trimmed -match "$publicSurfaceAccessibilityPattern\s+.*\benum\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b") {
                $pendingEnumName = $Matches.name
            }

            if ($null -ne $pendingEnumName -and $trimmed.Contains("{")) {
                $enumName = $pendingEnumName
                $pendingEnumName = $null
                $braceDepth = 1
                $afterOpen = $trimmed.Substring($trimmed.IndexOf("{", [StringComparison]::Ordinal) + 1)
                Add-EnumMemberFragments $afterOpen $enumName $Api
                if ($trimmed.Contains("}")) {
                    $braceDepth = 0
                    $enumName = $null
                }
            }

            continue
        }

        $memberText = $trimmed
        if ($memberText.Contains("}")) {
            $memberText = $memberText.Substring(0, $memberText.IndexOf("}", [StringComparison]::Ordinal))
            $braceDepth--
        }

        Add-EnumMemberFragments $memberText $enumName $Api
        if ($braceDepth -le 0) {
            $enumName = $null
        }
    }
}

function Add-EnumMemberFragments([string] $Text, [string] $EnumName, [System.Collections.Generic.HashSet[string]] $Api) {
    foreach ($fragment in ($Text -split ",")) {
        $candidate = $fragment.Trim()
        if ([string]::IsNullOrWhiteSpace($candidate) -or
            $candidate.StartsWith("[", [StringComparison]::Ordinal) -or
            $candidate.StartsWith("{", [StringComparison]::Ordinal)) {
            continue
        }

        if ($candidate -match "^(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<value>\s*=\s*.+)?$") {
            $entry = "public enum $EnumName.$($Matches.name)"
            if (-not [string]::IsNullOrWhiteSpace($Matches.value)) {
                $entry = "$entry $($Matches.value.Trim())"
            }

            [void] $Api.Add($entry)
        }
    }
}

function Get-PackageApi([hashtable] $Package) {
    $packagePath = Join-Path $root $Package.Path
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Package source directory does not exist: $packagePath"
    }

    $api = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    $files = Get-ChildItem -LiteralPath $packagePath -Recurse -File -Filter "*.cs" |
        Where-Object {
            $_.FullName -notmatch "[\\/](bin|obj)[\\/]" -and
            $_.Name -notlike "*.g.cs"
        }

    foreach ($file in $files) {
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $containingTypePublic = Get-ContainingTypePublicFlags $lines
        Add-EnumMembers $lines $api $containingTypePublic

        $pendingDeclaration = $null
        $pendingParenDepth = 0
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            if ($null -ne $pendingDeclaration) {
                $pendingDeclaration = "$pendingDeclaration`n$line"
                $pendingParenDepth += Get-ParenthesisDelta $line
                if ($pendingParenDepth -le 0) {
                    $pendingDeclaration = Add-GenericConstraints $pendingDeclaration $lines ($index + 1) ([ref] $index)
                    $apiLine = Normalize-ApiDeclaration $pendingDeclaration
                    if ($null -ne $apiLine) {
                        [void] $api.Add($apiLine)
                    }

                    $pendingDeclaration = $null
                    $pendingParenDepth = 0
                }

                continue
            }

            # Skip declarations whose containing type is not effectively public; only
            # members of public/protected types form the supported consumer surface.
            if (-not $containingTypePublic[$index]) {
                continue
            }

            $apiLine = Normalize-ApiLine $line
            if ($null -eq $apiLine) {
                continue
            }

            $parenDepth = Get-ParenthesisDelta $line
            if ($parenDepth -gt 0 -and $apiLine -notmatch "=>") {
                $pendingDeclaration = $line
                $pendingParenDepth = $parenDepth
                continue
            }

            if ($apiLine.Contains("<", [StringComparison]::Ordinal)) {
                $apiLine = Normalize-ApiDeclaration (Add-GenericConstraints $line $lines ($index + 1) ([ref] $index))
            }

            [void] $api.Add($apiLine)
        }
    }

    return @($api | Sort-Object)
}

function BaselinePath([string] $PackageId) {
    return Join-Path $baselineRoot "$PackageId.txt"
}

function Read-Baseline([string] $Path, [string] $PackageId) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing public API baseline for $PackageId at $Path. Run scripts/check-api-compat-baseline.ps1 -Update for an intentional baseline refresh."
    }

    return @(Get-Content -LiteralPath $Path |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not $_.TrimStart().StartsWith("#", [StringComparison]::Ordinal)
        })
}

function Write-Baseline([string] $Path, [string] $PackageId, [string[]] $Api) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $content = @(
        "# DotBoxD.Kernels public API baseline",
        "# Package: $PackageId",
        "# Update intentionally with scripts/check-api-compat-baseline.ps1 -Update when approving public API changes.",
        ""
    ) + $Api
    Set-Content -LiteralPath $Path -Value $content
}

function Compare-Baseline([string] $PackageId, [string[]] $Expected, [string[]] $Actual) {
    $expectedSet = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    foreach ($item in $Expected) {
        [void] $expectedSet.Add($item)
    }

    $actualSet = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::Ordinal)
    foreach ($item in $Actual) {
        [void] $actualSet.Add($item)
    }

    $removed = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $added = @($Actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($removed.Count -eq 0 -and $added.Count -eq 0) {
        return
    }

    $details = New-Object "System.Collections.Generic.List[string]"
    if ($removed.Count -gt 0) {
        $details.Add("Removed API:")
        foreach ($item in $removed) {
            $details.Add("  - $item")
        }
    }

    if ($added.Count -gt 0) {
        $details.Add("Added API:")
        foreach ($item in $added) {
            $details.Add("  + $item")
        }
    }

    throw "Public API baseline mismatch for $PackageId.`n$($details -join [Environment]::NewLine)`nIf this is intentional, update the baseline and document the versioning decision."
}

# When the script is dot-sourced (for example by tests), only its functions are
# defined; the baseline check is not executed. Normal invocation (&, direct call, or
# CI) still runs the gate below.
if ($MyInvocation.InvocationName -eq ".") {
    return
}

if ($Update) {
    foreach ($package in $packages) {
        $api = Get-PackageApi $package
        Write-Baseline (BaselinePath $package.Id) $package.Id $api
    }

    Write-Host "Public API baselines updated. Packages: $($packages.Count)"
    return
}

foreach ($package in $packages) {
    $actual = Get-PackageApi $package
    $expected = Read-Baseline (BaselinePath $package.Id) $package.Id
    Compare-Baseline $package.Id $expected $actual
}

Write-Host "Public API baseline check passed. Packages: $($packages.Count)"
