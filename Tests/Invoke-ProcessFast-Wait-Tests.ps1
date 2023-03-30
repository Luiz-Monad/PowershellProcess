
function Write-Object {
    [CmdLetBinding()]
    param (
        [string]$tag, 
        [Parameter(ValueFromPipeline)][object]$obj
    )
    begin {
        $ix = 0;
    }
    process {
        $ntag = "$($tag)[$ix]:"
        if ($null -eq $obj) {
            Write-Host ( "$($ntag)[null]" )
            return;
        }
        Write-Host ( "$($ntag)$($obj.GetType().FullName)" )
        if (($obj -is [System.Collections.IList]) -or ($obj -is [System.Array])) {
            $obj | Write-Object -Tag $ntag
        }
        else {
            Write-Host ( $obj | Format-Table | Out-String )
        }
        $ix = $ix + 1
    }
}

function  Test {
    param (
        [scriptblock]$sb
    )
    Write-Host -ForegroundColor Cyan "Testing $sb"
    & $sb *>&1 | Write-Object -Tag ""
    Write-Host -ForegroundColor Green "Ok!"
    Write-Host ""
}

#########################################################################################################################################################

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -Wait -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }

#########################################################################################################################################################

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -Wait -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }

#########################################################################################################################################################

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -Wait -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }
