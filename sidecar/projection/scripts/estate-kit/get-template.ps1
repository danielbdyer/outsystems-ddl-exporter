<#
THE ESTATE KIT — fetch the newest proving template + manifest, verified.

    .\get-template.ps1 -From <dir> [-Out <dir>]
    .\get-template.ps1 -Organization <org-url> -Project <p> -Feed <f> -Package <n> [-Out <dir>]

Two channels. -From is the share-path / local-directory source — the
day-one fallback. The feed parameters pull the latest version from the
Azure Artifacts Universal Packages feed the nightly publishes to (requires
the az CLI with the azure-devops extension, signed in). Either way the
pair is verified — the manifest's sha256 and byte count against the
artifact — before it lands in -Out (default .\templates), and the
template's identity is printed from the manifest.
#>
param(
    [string] $From,
    [string] $Organization,
    [string] $Project,
    [string] $Feed,
    [string] $Package,
    [string] $Out = '.\templates'
)
. (Join-Path $PSScriptRoot 'kit-common.ps1')

New-Item -ItemType Directory -Force -Path $Out | Out-Null

if ($From) {
    $pair = Get-NewestTemplate $From
    Copy-Item $pair.Bak, $pair.Manifest -Destination $Out -Force
}
elseif ($Organization) {
    if (-not ($Project -and $Feed -and $Package)) { Stop-Kit 'the feed channel needs -Project, -Feed, and -Package' }
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Stop-Kit 'the az CLI is not installed; use -From <dir> (the share-path fallback) until it is' }
    Write-KitLog "downloading the latest $Package from $Feed..."
    az artifacts universal download `
        --organization $Organization --project $Project --scope project `
        --feed $Feed --name $Package --version '*' --path $Out | Out-Null
    if ($LASTEXITCODE -ne 0) { Stop-Kit 'the feed download did not succeed' }
}
else {
    Stop-Kit 'name a source: -From <dir> or -Organization <org-url> ...'
}

$pair = Get-NewestTemplate $Out
$m = Test-TemplatePair $pair.Bak $pair.Manifest
Write-KitLog ("verified: {0} · commit {1} · data {2} · baked {3} · lane {4}" -f `
    $m.template, $m.commit.Substring(0, 8), $m.fingerprints.data.Substring(0, 8), $m.bakedAtUtc, $m.lane)
Write-KitLog "template: $($pair.Bak)"
