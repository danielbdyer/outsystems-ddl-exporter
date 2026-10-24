#Requires -Version 7.0
# The machine's shared SQL Server for tests and sessions on Windows (DECISIONS.md, 2026-10-19): SQL Server Express LocalDB's default
# instance, MSSQLLocalDB, which io/LocalServer chooses when neither DBCHANGE_SQL nor ~/.dbchange/sql.env names a server. It runs as the
# user and takes Windows authentication, so it holds no password. It has no SQL Server Agent, so CDC is never proven here. ci/sql.sh
# installs and starts SQL Server 2022 Express on Linux.
#   ci/sql.ps1 up     create MSSQLLocalDB when it is absent, start it, and wait until it answers
#   ci/sql.ps1 down   stop MSSQLLocalDB; its databases stay for the next up
#   ci/sql.ps1 conn   set DBCHANGE_SQL to MSSQLLocalDB for this PowerShell session (run it as ./ci/sql.ps1 conn); nothing is printed
param([Parameter(Mandatory)][ValidateSet('up', 'down', 'conn')][string]$Verb)
$ErrorActionPreference = 'Stop'

$instance = 'MSSQLLocalDB'
$server = "(localdb)\$instance"

function Fail([string]$message, [int]$code = 1) {
    [Console]::Error.WriteLine("ci/sql.ps1: $message")
    exit $code
}

function Up {
    if (-not $IsWindows) { Fail 'LocalDB runs on Windows alone; on Linux run ci/sql.sh up, or set DBCHANGE_SQL to another SQL Server' 4 }
    if (-not (Get-Command sqllocaldb -ErrorAction SilentlyContinue)) {
        Fail 'sqllocaldb is not on the PATH: install SQL Server Express LocalDB 2022 or later, or set DBCHANGE_SQL to another SQL Server' 4
    }
    sqllocaldb info $instance *> $null
    if ($LASTEXITCODE -ne 0) {
        sqllocaldb create $instance | Out-Host
        if ($LASTEXITCODE -ne 0) { Fail "sqllocaldb create $instance failed; its output is above" 4 }
    }
    sqllocaldb start $instance | Out-Host
    if ($LASTEXITCODE -ne 0) { Fail "sqllocaldb start $instance failed; its output is above" 4 }

    # sqllocaldb start returns once the instance runs; where sqlcmd is installed (windows-latest has it), a SELECT 1 confirms it answers.
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        "LocalDB ${instance}: started on $server"
        return
    }
    foreach ($attempt in 1..90) {
        $version = sqlcmd -S $server -E -b -l 5 -h -1 -W -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128))"
        if ($LASTEXITCODE -eq 0) {
            "LocalDB ${instance}: SQL Server $($version | Select-Object -First 1) on $server"
            return
        }
        Start-Sleep -Seconds 2
    }
    Fail "LocalDB $instance did not answer within three minutes; sqllocaldb info $instance names its state" 4
}

switch ($Verb) {
    'up' { Up }
    'down' {
        sqllocaldb stop $instance | Out-Null
        "LocalDB ${instance}: stopped; its databases stay for the next up"
    }
    'conn' { $env:DBCHANGE_SQL = "Server=$server;Integrated Security=true" }
}
