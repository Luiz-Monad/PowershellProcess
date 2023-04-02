param (
    [string]$ModuleName = 'PowerProcess',
    [string]$Configuration = 'Release',
    [string]$Framework = 'net6.0'
)

$ScriptPath = ($(try { $script:psEditor.GetEditorContext().CurrentFile.Path } catch {}), $script:MyInvocation.MyCommand.Path, $script:PSCommandPath, $(try { $script:psISE.CurrentFile.Fullpath.ToString() } catch {}) | % { if ($_ -ne '' ) { $_.ToLower() } } | Split-Path -EA 0 | Get-Unique ), $PSScriptRoot.ToLower() | Get-Unique
Set-Location -Path $ScriptPath
Write-Host "ScriptPath: $ScriptPath"

dotnet build .\$ModuleName\$ModuleName.csproj --configuration $Configuration --framework $Framework --no-self-contained
# Local development
Copy-Item -Destination $ScriptPath -Path "$ScriptPath\$ModuleName\$ModuleName.psd1" -Force
Copy-Item -Destination $ScriptPath -Path "$ScriptPath\$ModuleName\bin\$Configuration\$Framework\$ModuleName.dll" -Force

$Destination = "$ScriptPath\$ModuleName\bin\$Configuration\$Framework\$ModuleName\"

# Package
if (-NOT (Test-Path -Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination
}

Copy-Item -Destination $Destination -Path "$ScriptPath\README.md"
Copy-Item -Destination $Destination -Path "$ScriptPath\$ModuleName\$ModuleName.psd1"
Copy-Item -Destination $Destination -Path "$ScriptPath\$ModuleName\bin\$Configuration\$Framework\$ModuleName.dll"
