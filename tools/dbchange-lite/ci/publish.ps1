$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist/dbchange-lite'
$dacfx = (Select-String -Path (Join-Path $root 'Directory.Packages.props') -Pattern 'Include="Microsoft.SqlServer.DacFx" Version="([^"]*)"').Matches[0].Groups[1].Value
function Downloaded($name) { (Select-String -Path (Join-Path $root 'src/DbChangeLite.csproj') -Pattern ('Include="' + [regex]::Escape($name) + '" Version="\[([^\]]*)\]"')).Matches[0].Groups[1].Value }
$referenceAssemblies = Downloaded 'Microsoft.NETFramework.ReferenceAssemblies.net472'
$master = Downloaded 'Microsoft.SqlServer.Dacpacs.Master'
$msdb = Downloaded 'Microsoft.SqlServer.Dacpacs.Msdb'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $root 'src/DbChangeLite.csproj') -c Release -f net10.0 --no-self-contained -o $out -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$packages = ((dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim().TrimEnd('\', '/')
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacfx/$dacfx/lib/net10.0/Microsoft.Data.Tools.Schema.SqlTasks*.targets") $out
$from = Join-Path $packages "microsoft.netframework.referenceassemblies.net472/$referenceAssemblies/build/.NETFramework/v4.7.2"
New-Item -ItemType Directory -Force (Join-Path $out 'refasm/.NETFramework/v4.7.2/RedistList') | Out-Null
Copy-Item (Join-Path $from 'mscorlib.dll') (Join-Path $out 'refasm/.NETFramework/v4.7.2/')
Copy-Item (Join-Path $from 'RedistList/FrameworkList.xml') (Join-Path $out 'refasm/.NETFramework/v4.7.2/RedistList/')
$schemas = Join-Path $out 'Extensions/Microsoft/SQLDB/Extensions/SqlServer/160/SqlSchemas'
New-Item -ItemType Directory -Force $schemas | Out-Null
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacpacs.master/$master/tools/master.dacpac") $schemas
Copy-Item (Join-Path $packages "microsoft.sqlserver.dacpacs.msdb/$msdb/tools/msdb.dacpac") $schemas
Write-Output "dist/dbchange-lite published (DacFx $dacfx, reference assemblies $referenceAssemblies, master $master, msdb $msdb)"
