<#
THE ESTATE KIT — stand a proving copy up from the verified template.

    .\restore-proving-copy.ps1 [-Template <path.bak>] [-Name <db>] [-Templates <dir>]
                               [-Server <engine>] [-SqlUser <u> -SqlPassword <p>]

Restores the template (default: the newest under -Templates, default
.\templates) into the machine's engine (-Server, default LocalDB) as a
disposable copy under the PROTOCOL naming (PG_Proving_<8 hex> unless -Name
overrides), then prints the copy's own identity from [twin].[__state] — the
restored database answers which base it came from; nothing is taken on
trust. Integrated authentication unless -SqlUser/-SqlPassword are passed.
#>
param(
    [string] $Template,
    [string] $Name,
    [string] $Templates = '.\templates',
    [string] $Server = '(localdb)\MSSQLLocalDB',
    [string] $SqlUser,
    [string] $SqlPassword
)
. (Join-Path $PSScriptRoot 'kit-common.ps1')

if (-not $Template) {
    $pair = Get-NewestTemplate $Templates
    Test-TemplatePair $pair.Bak $pair.Manifest | Out-Null
    $Template = $pair.Bak
}
if (-not (Test-Path $Template)) { Stop-Kit "no template at $Template" }
if (-not $Name) { $Name = 'PG_Proving_' + ([guid]::NewGuid().ToString('N').Substring(0, 8)) }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
Restore-KitTemplate -Server $Server -Bak (Resolve-Path $Template).Path -Database $Name -SqlUser $SqlUser -SqlPassword $SqlPassword
$sw.Stop()

Write-KitLog ("restored {0} as [{1}] in {2:N2}s" -f (Split-Path -Leaf $Template), $Name, $sw.Elapsed.TotalSeconds)
Write-KitLog ("identity: " + (Get-KitIdentity -Server $Server -Database $Name -SqlUser $SqlUser -SqlPassword $SqlPassword))
Write-Output $Name
