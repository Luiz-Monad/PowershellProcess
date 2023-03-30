param (
    [string]$ModuleName = 'PowerProcess',
    [string]$NuGetApiKey,
    [switch]$Force
)

$ScriptPath = $(try { $script:psEditor.GetEditorContext().CurrentFile.Path } catch {}), $script:MyInvocation.MyCommand.Path, $script:PSCommandPath, $(try { $script:psISE.CurrentFile.Fullpath.ToString() } catch {}) | % { if ($_) {$_.ToLower() }} | Split-Path -EA 0 | Select-Object -Unique
if (-NOT $ScriptPath) { $ScriptPath = $PSScriptRoot }
Set-Location -Path $ScriptPath

if ($NuGetApiKey) { Publish-Module -NuGetApiKey $NuGetApiKey -Path "$ScriptPath\$ModuleName\bin\Release\net6.0\publish\$ModuleName" -Repository "PSGallery" -Force:$Force }
