<#
Fails when tracked (or staged) text files contain generic secret patterns.
When %APPDATA%\DevPilot\policy.local.json exists, its denyTerms are checked too;
matches report the file only, never the term.
Usage: scripts\check-secrets.ps1 [-Staged]
#>
param([switch]$Staged)

$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

# Same shapes as the 'secret' rule of the Egress Guard scanner.
$pattern = 'AKIA[0-9A-Z]{16}|-----BEGIN ([A-Z]+ )?PRIVATE KEY-----|ghp_[A-Za-z0-9]{36}|sk-[A-Za-z0-9]{20,}|(?i:api[_-]?key|secret|password|token)\s*[:=]\s*["''][^"'']{8,}["'']'

# Rule definitions and fake values in tests are expected to look like secrets.
$skip = @('^DevPilot\.Core/Guard/', '^DevPilot\.Tests/', '^scripts/check-secrets\.ps1$', '\.(ico|png|jpg|zip|dll)$')

$files = if ($Staged) { git diff --cached --name-only --diff-filter=ACM } else { git ls-files }

$terms = @()
$policy = Join-Path $env:APPDATA 'DevPilot\policy.local.json'
if ($env:APPDATA -and (Test-Path $policy)) {
    try {
        $json = Get-Content $policy -Raw | ConvertFrom-Json
        foreach ($name in 'denyTerms', 'deny', 'terms') {
            if ($json.PSObject.Properties.Name -contains $name) { $terms += @($json.$name) }
        }
    } catch { Write-Output 'note: policy.local.json could not be read; deny terms skipped.' }
}

# Same syntax as DevPilot: plain text, "*" = up to 40 characters, "re:" = regular expression.
function Test-DenyTerm([string]$text, [string]$term) {
    try {
        if ($term -like 're:*') { return [regex]::IsMatch($text, $term.Substring(3), 'IgnoreCase', [TimeSpan]::FromMilliseconds(500)) }
        $core = $term.Trim('*')
        if ($core.Length -eq 0) { return $false }
        if ($core.IndexOf('*') -lt 0) { return $text.IndexOf($core, [StringComparison]::OrdinalIgnoreCase) -ge 0 }
        $pattern = ($core.Split('*') | ForEach-Object { [regex]::Escape($_) }) -join '.{0,40}?'
        return [regex]::IsMatch($text, $pattern, 'IgnoreCase', [TimeSpan]::FromMilliseconds(500))
    } catch { return $false }
}

$bad = 0
foreach ($f in $files) {
    if (-not $f -or ($skip | Where-Object { $f -match $_ })) { continue }
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { continue }
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction SilentlyContinue
    if (-not $text) { continue }
    if ($text -cmatch $pattern) {
        Write-Output "::error file=$f::Possible embedded secret"
        $bad++
    }
    foreach ($t in $terms) {
        if ($t -and (Test-DenyTerm $text ([string]$t))) {
            Write-Output "::error file=$f::Matches a local deny term"
            $bad++
            break
        }
    }
}
if ($bad -gt 0) { Write-Output "$bad file(s) need attention."; exit 1 }
Write-Output 'No secret patterns found.'
