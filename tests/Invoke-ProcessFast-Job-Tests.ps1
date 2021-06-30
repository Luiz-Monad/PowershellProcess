
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

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0; 1; 2; 3') -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }

#########################################################################################################################################################

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', '0/0; 0/0') -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }

#########################################################################################################################################################

Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -WrapOutputStream -OutputBuffer 1 }


Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -MergeStandardErrorToOutput }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -MergeStandardErrorToOutput -OutputBuffer 1 }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -MergeStandardErrorToOutput -WrapOutputStream }
Test { Invoke-ProcessFast -FilePath pwsh -ArgumentList @('-noprofile', '-command', 'Write-Error "test"; Write-Host "ok"') -MergeStandardErrorToOutput -WrapOutputStream -OutputBuffer 1 }
