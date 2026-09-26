# LAWS.md from the tests, as ci/laws.sh writes it, byte for byte (DECISIONS.md, 2026-09-25): three tables, one row per test method
# carrying a [Trait("Law", "<the law>")], a [Trait("Value", "<a VALUES.md row>")] or a [Trait("Exit", "M<n>.<k>")], with the trait's
# value, the method's name in English and its full name; after the values, the rows of VALUES.md no test declares, and after the
# exits, the exits of V3_MILESTONES.md no test declares. Laws sort ordinally by law and then by full name; values by the row's
# letter, then its number as a number, then the full name; exits by milestone, then exit, then the full name. The generated banner
# comes first and no timestamp. Usage: ci/laws.ps1 [output], LAWS.md at the root by default.
param([string]$Output)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Output) { $Output = Join-Path $root 'LAWS.md' }
$utf8 = [Text.UTF8Encoding]::new($false)
$invariant = [Globalization.CultureInfo]::InvariantCulture

$files = Get-ChildItem -Path (Join-Path $root 'tests') -Recurse -File -Filter '*.cs' |
  ForEach-Object { [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/') } |
  Where-Object { $_ -notlike 'tests/Golden/*' -and $_ -notmatch '/(bin|obj)/' }
$files = [string[]]@($files); [Array]::Sort($files, [StringComparer]::Ordinal)

# Each row: the trait's name, its sort key, its value, the test's full name and its English name, tab-separated, sorted ordinally.
# A comment line is skipped; a class is the last one declared at the start of a line, since a nested helper class, indented, never holds a test.
$rows = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
  $space = ''; $type = ''; $pending = [Collections.Generic.List[string[]]]::new()
  foreach ($line in [IO.File]::ReadAllLines((Join-Path $root $file), $utf8)) {
    if ($line -cmatch '^[ \t]*//') { continue }
    if ($line -cmatch '^namespace ([A-Za-z0-9_.]+);') { $space = $Matches[1] }
    if ($line -cmatch '^((public|internal|private|sealed|static|abstract|partial|file)[ \t]+)*class[ \t]+([A-Za-z0-9_]+)') { $type = $Matches[3] }
    if ($line -cmatch '\[Trait\("(Law|Value|Exit)", "([^"]+)"\)\]') {
      $kind = $Matches[1]; $value = $Matches[2]
      $key = switch ($kind) {
        'Value' { $value.Substring(0, 1) + ([int]$value.Substring(1)).ToString('000', $invariant) }
        'Exit' { $parts = $value.Substring(1).Split('.'); ([int]$parts[0]).ToString('00', $invariant) + '.' + ([int]$parts[1]).ToString('00', $invariant) }
        default { $value }
      }
      $pending.Add(@($kind, $key, $value))
    }
    if ($pending.Count -gt 0 -and $line -cmatch '^[ \t]*public (async )?[A-Za-z0-9_<>.]+ ([A-Za-z0-9_]+)\(') {
      $method = $Matches[2]
      $english = ($method -creplace '_s_', "'s_") -creplace '_', ' '
      foreach ($trait in $pending) { $rows.Add("$($trait[0])`t$($trait[1])`t$($trait[2])`t$space.$type.$method`t$english") }
      $pending.Clear()
    }
  }
}

$sorted = [string[]]$rows.ToArray(); [Array]::Sort($sorted, [StringComparer]::Ordinal)
$declaredValues = @{}; $declaredExits = @{}
foreach ($row in $sorted) {
  $cells = $row.Split("`t")
  if ($cells[0] -ceq 'Value') { $declaredValues[$cells[2]] = $true } elseif ($cells[0] -ceq 'Exit') { $declaredExits[$cells[2]] = $true }
}

# The rows of VALUES.md (| S2 | …) no Value trait names, by letter and then number.
$undeclaredValues = [Collections.Generic.List[string]]::new()
foreach ($line in [IO.File]::ReadAllLines((Join-Path $root 'VALUES.md'), $utf8)) {
  if ($line -cmatch '^\| ([A-Z][0-9]+) \|' -and -not $declaredValues.ContainsKey($Matches[1])) {
    $id = $Matches[1]
    $undeclaredValues.Add($id.Substring(0, 1) + ([int]$id.Substring(1)).ToString('000', $invariant) + "`t" + $id)
  }
}

# The exits of V3_MILESTONES.md no Exit trait names: the numbered list after **Exit.** in each section headed M<n>, or exit 1 where the exit is a paragraph.
$undeclaredExits = [Collections.Generic.List[string]]::new()
$m = -1; $reading = $false; $n = 0
foreach ($line in [IO.File]::ReadAllLines((Join-Path $root 'V3_MILESTONES.md'), $utf8)) {
  if ($line -cmatch '^## [0-9]+\. M([0-9])') { $m = [int]$Matches[1] }
  elseif ($line.StartsWith('**Exit.**')) { $reading = $true; $n = 0 }
  elseif ($reading -and $line -cmatch '^[0-9]+\. ') { $n++ }
  elseif ($reading -and $line.Length -eq 0) {
    for ($k = 1; $k -le [Math]::Max($n, 1); $k++) {
      if (-not $declaredExits.ContainsKey("M$m.$k")) { $undeclaredExits.Add($m.ToString('00', $invariant) + '.' + $k.ToString('00', $invariant) + "`tM$m.$k") }
    }
    $reading = $false
  }
}

function Joined([Collections.Generic.List[string]]$keyed) {
  $sortedKeyed = [string[]]$keyed.ToArray(); [Array]::Sort($sortedKeyed, [StringComparer]::Ordinal)
  $names = @($sortedKeyed | ForEach-Object { $_.Split("`t")[1] })
  if ($names.Count -eq 0) { 'none' } else { $names -join ', ' }
}

$text = [Text.StringBuilder]::new()
function Table([string]$kind, [string]$heading) {
  [void]$text.Append("| $heading | The test, in English | The test |`n|---|---|---|`n")
  foreach ($row in $sorted) {
    $cells = $row.Split("`t")
    if ($cells[0] -ceq $kind) { [void]$text.Append("| $($cells[2]) | $($cells[4]) | ``$($cells[3])`` |`n") }
  }
}

[void]$text.Append("<!-- generated by ci/laws.sh, or ci/laws.ps1 on Windows, from the tests' Law, Value and Exit traits; do not edit -->`n# Laws`n`n")
[void]$text.Append("One row per test that states a law, from its ``[Trait(`"Law`", …)]``: the law, the test's name in English, and its full name.`n")
[void]$text.Append("Whether each is green is the CI run's to say. A law without a green test is not a law.`n`n")
Table 'Law' 'Law'
[void]$text.Append("`n## Values`n`n")
[void]$text.Append("One row per test that holds a row of ``VALUES.md``, from its ``[Trait(`"Value`", …)]``.`n`n")
Table 'Value' 'Value'
[void]$text.Append("`nRows of ``VALUES.md`` no test declares: $(Joined $undeclaredValues).`n")
[void]$text.Append("`n## Milestone exits`n`n")
[void]$text.Append("One row per test that runs an exit of ``V3_MILESTONES.md``, from its ``[Trait(`"Exit`", …)]``, as M<n>.<k>.`n`n")
Table 'Exit' 'Exit'
[void]$text.Append("`nExits no test declares, which a person or a CI job runs: $(Joined $undeclaredExits).`n")
[IO.File]::WriteAllText([IO.Path]::GetFullPath($Output), $text.ToString(), $utf8)
