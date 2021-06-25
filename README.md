
# PowerProcess

This class provides a faster replacement for Powershell `&` and `Start-Process`.

# Motive

The default implementation of powershell error stream allocates lots of objects and its very slow if you need to process the redirect error stream as strings, it also processes line by line with has a high burden on the pipeline.

Other important problem is that the default implementation until `Powershell 7.1.3` internally uses `Arguments` instead of `ArgumentList` besides being called ArgumentList, it is a `[string]`, which is unfortunate because of historical reasons.

# Improvements

* Added support for buffering `output` and `error` streams.
* Its not possible to call the shell by mistake.
* Support for the new VT terminals [WIP].
* Correct treatment of argument lists by the direct use of `System.Diagnostics.Process`.
* Possibility of merging `output` and `error` at the source.

## Install

Available in PSGallery: [https://www.powershellgallery.com/packages/PowerProcess](https://www.powershellgallery.com/packages/PowerProcess)

```pwsh
Install-Module -Name PowerProcess
```

## Usage

```pwsh
$ Get-Help Invoke-ExternalCommand -Full
NAME
    Invoke-ExternalCommand

SYNOPSIS
    This is a helper function that runs a external executable binary and support faster stream redirects.


SYNTAX
    Invoke-ExternalCommand [-Command] <String> [[-Arguments] <String[]>] [[-HideArguments]
    <Int32[]>] [[-DontEscapeArguments] <Int32[]>] [[-OutVarStdout] <String>]
    [[-OutVarStderr] <String>] [[-OutVarCode] <String>] [-Return] [-IgnoreExitCode]
    [-HideStdout] [-HideStderr] [-HideCommand] [<CommonParameters>]


DESCRIPTION


PARAMETERS
    -Command <String>
        Executable name/command to run. Must be available at PATH env var or you can specify
        full path to the binary.

        Required?                    true
        Position?                    1
        Default value
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -Arguments <String[]>
        Arguments string array to path to Command as arguments

        Required?                    false
        Position?                    2
        Default value                @()
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -HideArguments <Int32[]>
        List indexes (starting with 0) of arguments you would like to obscure in the message
        that command and its
        arguments that being executed.

        Required?                    false
        Position?                    3
        Default value                @()
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -DontEscapeArguments <Int32[]>
        List indexes (starting with 0) of arguments you would like to skip escape logic on.
        When used, unless is a simple argument it's on you to escape it correctly

        Required?                    false
        Position?                    4
        Default value                @()
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -OutVarStdout <String>
        Provide variable name that will be used to save STDOUT output of the command
        execution.

        Required?                    false
        Position?                    5
        Default value
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -OutVarStderr <String>
        Provide variable name that will be used to save STDERR output of the command
        execution.

        Required?                    false
        Position?                    6
        Default value
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -OutVarCode <String>
        Provide variable name that will be used to save process exit code.

        Required?                    false
        Position?                    7
        Default value
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -Return [<SwitchParameter>]
        By default this function returns $null, if specified you will get this object:
        @{Stdout="Contains Stdout",Stderr="Contains Stderr",All="Stdout and Stderr output as
        it was generated",Code="Int from process exit code"}
        Stdout output sequence order is guaranteed, while Stderr lines sequence might be out
        of order (eventing nature?).

        Required?                    false
        Position?                    named
        Default value                False
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -IgnoreExitCode [<SwitchParameter>]
        Specify if you expect non 0 exit code from the Command and would like to avoid non 0
        exit code exception.

        Required?                    false
        Position?                    named
        Default value                False
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -HideStdout [<SwitchParameter>]
        Specify if don't want STDOUT to be written to the host

        Required?                    false
        Position?                    named
        Default value                False
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -HideStderr [<SwitchParameter>]
        Specify if don't want STDERR to be written to the host

        Required?                    false
        Position?                    named
        Default value                False
        Accept pipeline input?       false
        Accept wildcard characters?  false

    -HideCommand [<SwitchParameter>]
        Specify if don't want `Running command` informational message to be written to the
        host STDERR

        Required?                    false
        Position?                    named
        Default value                False
        Accept pipeline input?       false
        Accept wildcard characters?  false

    <CommonParameters>
        This cmdlet supports the common parameters: Verbose, Debug,
        ErrorAction, ErrorVariable, WarningAction, WarningVariable,
        OutBuffer, PipelineVariable, and OutVariable. For more information, see
        about_CommonParameters (https:/go.microsoft.com/fwlink/?LinkID=113216).

INPUTS

OUTPUTS
    System.Collections.Hashtable


    -------------------------- EXAMPLE 1 --------------------------

    >Invoke-ExternalCommand -Command git -Arguments version

    Running command [ C:\Program Files\Git\cmd\git.exe ] with arguments: "version"
    git version 2.20.1.windows.1

    > Invoke-ExternalCommand -Command helm -Arguments version,--client
    Running command [ C:\ProgramData\chocolatey\bin\helm.exe ] with arguments: "version"
    "--client"
    Client: &version.Version{SemVer:"v2.12.2",
    GitCommit:"7d2b0c73d734f6586ed222a567c5d103fed435be", GitTreeState:"clean"}

    > Invoke-ExternalCommand -Command helm -Arguments versiondd,--client
    Running command [ C:\ProgramData\chocolatey\bin\helm.exe ] with arguments: "versiondd"
    "--client"
    Error: unknown command "versiondd" for "helm"
    Did you mean this?


        version
    Run 'helm --help' for usage.

    Command returned non zero exit code of '1'. Command: helm
    At C:\Users\user\dev\ps-modules\Igloo.Powershell.ExternalCommand\Igloo.Powershell.Externa
    lCommand.psm1:237 char:9
    +         throw ([string]::Format("Command returned non zero exit code  ...
    +         ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
    + CategoryInfo          : OperationStopped: (Command returne.... Command: helm:String)
    [], RuntimeException
    + FullyQualifiedErrorId : Command returned non zero exit code of '1'. Command: helm





RELATED LINKS
```

# Related Projects and scripts

This provides a way to return all the output as error when the process `ExitCode` is non-zero. But it saves `output` and `error` as temporary files, so we lose streamming, which is no good.
It also doesn't solve the problem with escaping arguments.

[Adam Bertram blog post](https://adamtheautomator.com/start-process/)

[Invoke-Process on the gallery](https://www.powershellgallery.com/packages/Invoke-Process/1.4/Content/Invoke-Process.ps1)

-------------------------------------------------

This person provides a better implementation of Invoke-Process than Adam's one, because it uses `BeginOutputReadLine` and `BeginErrorReadLine` instead of temporary files.
But it also suffers from the same problems, its a bit better in that it directly uses `System.Diagnostics.Process`

[guitarrapc_tech blog post](https://tech.guitarrapc.com/entry/2014/12/14/075248)

[Invoke-Process on their utils github repo](https://github.com/guitarrapc/PowerShellUtil/tree/master/Invoke-Process)

-------------------------------------------------

This also suffer from the same problems, its bit simpler implementation worth mentioning

[Relevant discussion](https://bleepcoder.com/powershell/648485701/call-native-operator)

[Invoke-NativeCommand mentioned](https://gist.github.com/indented-automation/fba795c43ef5a53483398cdc72ab7fa0)

------------------------------------------------

And the final implementation I found was this in a citation from `mklement0` in one of the discussions on github issues.
It solves the problem with the arguments, but does play well with streams, it does the right thing in relation with ordering, but doesn't play well with the powershell pipeline, it redirects the entire buffer to a variable, which is not that nice if you want realtime processing as the process generates the output.

[Invoke-ExternalCommand](https://github.com/choovick/ps-invoke-externalcommand)

