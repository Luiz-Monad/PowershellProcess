[CmdletBinding()]
param (
    [Parameter(Mandatory = 1)][string]$NuGetApiKey,
    [switch]$Force
)

Publish-Module -NuGetApiKey $NuGetApiKey -Path "$PSScriptRoot/PowerProcess" -Repository "PSGallery" -Force:$Force