#Requires -Version 7.0
# The machine's shared SQL Server for tests and sessions (V3_MILESTONES.md WP 0.7, section 1 fact 12): one container, dbchange-sql,
# from the image pinned by tag and digest, SQL Server Agent on (CDC needs it), published on 127.0.0.1 only. Its SA password
# is generated once per machine and kept only in ~/.dbchange/sql.env; nothing here prints it. ci/sql.sh is the same elsewhere.
#   ci/sql.ps1 up     pull the image when absent, make the container again when it runs another image, create or start it,
#                     wait until SQL Server answers
#   ci/sql.ps1 down   remove the container; the password stays for the next up
#   ci/sql.ps1 conn   set DBCHANGE_SQL for this PowerShell session (run it as ./ci/sql.ps1 conn); nothing is printed
param([Parameter(Mandatory)][ValidateSet('up', 'down', 'conn')][string]$Verb)
$ErrorActionPreference = 'Stop'

$image = 'mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090'
$name = 'dbchange-sql'
$dir = Join-Path $HOME '.dbchange'
$envFile = Join-Path $dir 'sql.env'
$lock = Join-Path $dir 'sql.lock'
$script:held = $false

function Fail([string]$message, [int]$code = 1) {
    if ($script:held) { Remove-Item -Force $lock -ErrorAction SilentlyContinue }
    [Console]::Error.WriteLine("ci/sql.ps1: $message")
    exit $code
}

function Value([string]$key) {
    if (Test-Path $envFile) { (Get-Content $envFile | Where-Object { $_.StartsWith("$key=") } | Select-Object -First 1) -replace "^$key=", '' }
}

function State {
    $status = docker container inspect --format '{{.State.Status}}' $name 2>$null
    if ($LASTEXITCODE -eq 0) { $status } else { '' }
}

# One up at a time per machine, so two runs never make two passwords for one container: the lock is a file created
# exclusively (CreateNew here, noclobber in ci/sql.sh). A lock older than ten minutes was left by a killed run.
function Lock {
    New-Item -ItemType Directory -Force $dir | Out-Null
    foreach ($attempt in 1..180) {
        try { [IO.File]::Open($lock, 'CreateNew').Dispose(); $script:held = $true; return } catch [IO.IOException] { }
        if ((Test-Path $lock) -and (Get-Item $lock).LastWriteTime -lt (Get-Date).AddMinutes(-10)) { Remove-Item -Force $lock -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
    }
    Fail "$lock has been held for three minutes; remove it if no other ci/sql run is going"
}

function Up {
    docker info *> $null
    if ($LASTEXITCODE -ne 0) { Fail 'Docker does not answer (docker info): start Docker, or set DBCHANGE_SQL to another SQL Server' 4 }
    Lock
    docker image inspect $image *> $null
    if ($LASTEXITCODE -ne 0) {
        docker pull $image | Out-Host
        if ($LASTEXITCODE -ne 0) { Fail "docker pull $image failed; its output is above" }
    }
    # A container made from another image than the pinned one (an older pin, or one made by hand) is removed and made again from the
    # pinned image, so the image a copy ran in is the one pinned; the copies it held go with it, and sql.env keeps the password.
    if ((State) -and (docker container inspect --format '{{.Image}}' $name) -ne (docker image inspect --format '{{.Id}}' $image)) {
        [Console]::Error.WriteLine("ci/sql.ps1: $name runs another image than $image; it is made again from the pinned image")
        docker rm -f $name *> $null
    }
    if (-not (State)) {
        $password = Value 'MSSQL_SA_PASSWORD'
        if (-not $password) { $password = 'Est!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(16)).ToLowerInvariant() }
        $port = if ($env:DBCHANGE_SQL_PORT) { $env:DBCHANGE_SQL_PORT } else { '11433' }
        [IO.File]::WriteAllText($envFile, "MSSQL_SA_PASSWORD=$password`nDBCHANGE_SQL_PORT=$port`n")
        # Its owner alone reads the password, as ci/sql.sh's umask and chmod make it; Windows keeps no such mode, and the file inherits
        # the private ACL of the user's profile folder there.
        if (-not $IsWindows) { [IO.File]::SetUnixFileMode($envFile, [IO.UnixFileMode]'UserRead, UserWrite') }
        docker run -d --name $name --env-file $envFile -e ACCEPT_EULA=Y -e MSSQL_AGENT_ENABLED=true -p "127.0.0.1:${port}:1433" $image | Out-Null
        if ($LASTEXITCODE -ne 0) {
            docker rm -f $name *> $null
            Fail "$name did not start on 127.0.0.1:${port}; if the port is taken: `$env:DBCHANGE_SQL_PORT = <a free port>; ./ci/sql.ps1 up"
        }
    }
    elseif (-not (Value 'MSSQL_SA_PASSWORD')) { Fail "$name exists but $envFile holds no password for it: ./ci/sql.ps1 down, then ./ci/sql.ps1 up" 6 }
    elseif ((State) -ne 'running') { docker start $name | Out-Null }

    # The password stays inside the container: sqlcmd's SELECT 1 runs there and reads the container's own environment.
    foreach ($attempt in 1..90) {
        if ((State) -ne 'running') { docker logs --tail 20 $name | Out-Host; Fail "$name stopped; its last lines are above" }
        docker exec $name /bin/sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P $MSSQL_SA_PASSWORD -b -Q ''SELECT 1'' >/dev/null 2>&1'
        if ($LASTEXITCODE -eq 0) {
            Remove-Item -Force $lock
            "${name}: SQL Server on 127.0.0.1,$(Value 'DBCHANGE_SQL_PORT'); the SA password is in $envFile"
            return
        }
        Start-Sleep -Seconds 2
    }
    Fail "SQL Server in $name did not answer within three minutes"
}

switch ($Verb) {
    'up' { Up }
    'down' {
        Lock
        docker rm -f $name *> $null
        Remove-Item -Force $lock
        "${name}: removed; $envFile keeps the password for the next up"
    }
    'conn' {
        if (-not (Value 'MSSQL_SA_PASSWORD')) { Fail "$envFile holds no password: ./ci/sql.ps1 up first" }
        $env:DBCHANGE_SQL = "Server=127.0.0.1,$(Value 'DBCHANGE_SQL_PORT');User ID=sa;Password=$(Value 'MSSQL_SA_PASSWORD');TrustServerCertificate=True"
    }
}
