using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Management.Automation;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nito.AsyncEx;
using System.Text;
using System.Management.Automation.Language;

namespace PowerProcess
{

    /// <summary>
    /// This class implements the Start-process command.
    /// </summary>
    /// <remarks>
    /// Monad2021: 
    ///   Added support for buffering streams.
    ///   Its not possible to call the shell by mistake.
    ///   Support for the new VT terminals [WIP].
    ///   Correct treatment of argument lists (just use Sys.Diag.Proc).
    ///   Possibility of merging Out and Error at the source.
    /// </remarks>
    [Cmdlet(VerbsLifecycle.Invoke, "ProcessFast", DefaultParameterSetName = "Default", SupportsShouldProcess = true, HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2097141")]
    [OutputType(typeof(Process))]
    public sealed class InvokeProcessFastCommand : PSCmdlet, IDisposable
    {
        private Process? _process = null;
        private ManualResetEvent? _waitHandle = null;
        private CancellationTokenSource? _cancellationSource = null;

        #region Parameters

        /// <summary>
        /// Path/FileName of the process to start.
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        [ValidateNotNullOrEmpty]
        [Alias("PSPath", "Path")]
        public string FilePath { get; set; }

        /// <summary>
        /// Arguments for the process.
        /// </summary>
        [Parameter(Position = 1)]
        [Alias("Args")]
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public string[] ArgumentList { get; set; }

        /// <summary>
        /// Credentials for the process.
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("RunAs")]
        [ValidateNotNullOrEmpty]
        [Credential]
        public PSCredential Credential { get; set; }

        /// <summary>
        /// Working directory of the process.
        /// </summary>
        [Parameter]
        [ValidateNotNullOrEmpty]
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Load user profile from registry.
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        [Alias("Lup")]
        public SwitchParameter LoadUserProfile { get; set; }

        /// <summary>
        /// PassThru parameter.
        /// </summary>
        [Parameter]
        public SwitchParameter PassThru { get; set; }

        /// <summary>
        /// Redirect outputs.
        /// </summary>
        [Parameter]
        [Alias("NoRedir")]
        public SwitchParameter DontRedirectOutputs { get; set; }

        /// <summary>
        /// Merge Error to Output.
        /// </summary>
        [Parameter]
        [Alias("Merge")]
        public SwitchParameter MergeStandardErrorToOutput { get; set; }

        /// <summary>
        /// Wrap output stream.
        /// </summary>
        [Parameter]
        [Alias("Obj")]
        public SwitchParameter WrapOutputStream { get; set; }

        /// <summary>
        /// Wait for the process to terminate.
        /// </summary>
        [Parameter]
        public SwitchParameter Wait { get; set; }

        /// <summary>
        /// Default Environment.
        /// </summary>
        [Parameter(ParameterSetName = "Default")]
        public SwitchParameter UseNewEnvironment { get; set; }

        #endregion

        #region Pipeline

        [Parameter(ValueFromPipeline = true)]
        public string InputObject { get; set; }

        /// <summary>
        /// Buffer the output stream.
        /// </summary>
        [Parameter]
        public int? OutputBuffer { get; set; }

        #endregion

        #region overrides

        /// <summary>
        /// BeginProcessing.
        /// </summary>
        protected override void BeginProcessing()
        {

            ProcessStartInfo startInfo = new ProcessStartInfo();

            // Path = Mandatory parameter -> Will not be empty.
            try
            {
                CommandInfo cmdinfo = base.InvokeCommand.GetCommand(
                    FilePath, CommandTypes.Application | CommandTypes.ExternalScript);
                startInfo.FileName = cmdinfo.Definition;
            }
            catch (CommandNotFoundException)
            {
                startInfo.FileName = FilePath;
            }

            if (ArgumentList != null)
            {
                foreach (var arg in ArgumentList)
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }

            if (WorkingDirectory != null)
            {
                // WorkingDirectory -> Not Exist -> Throw Error
                WorkingDirectory = ResolveFilePath(WorkingDirectory);
                if (!Directory.Exists(WorkingDirectory))
                {
                    var message = StringUtil.Format(ProcessResources.InvalidInput, "WorkingDirectory");
                    ErrorRecord er = new ErrorRecord(new DirectoryNotFoundException(message), "DirectoryNotFoundException", ErrorCategory.InvalidOperation, null);
                    WriteError(er);
                    return;
                }

                startInfo.WorkingDirectory = WorkingDirectory;
            }
            else
            {
                // Working Directory not specified -> Assign Current Path.
                startInfo.WorkingDirectory = base.SessionState.Path.CurrentFileSystemLocation.Path;
            }

            if (this.ParameterSetName.Equals("Default"))
            {
                startInfo.UseShellExecute = false;

                if (UseNewEnvironment)
                {
                    startInfo.EnvironmentVariables.Clear();
                    LoadEnvironmentVariable(startInfo, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine));
                    LoadEnvironmentVariable(startInfo, Environment.GetEnvironmentVariables(EnvironmentVariableTarget.User));
                }

                startInfo.CreateNoWindow = true;
#if !UNIX
                startInfo.LoadUserProfile = LoadUserProfile;
#endif
                if (Credential != null)
                {
                    NetworkCredential nwcredential = Credential.GetNetworkCredential();
                    startInfo.UserName = nwcredential.UserName;
                    if (string.IsNullOrEmpty(nwcredential.Domain))
                    {
                        startInfo.Domain = ".";
                    }
                    else
                    {
                        startInfo.Domain = nwcredential.Domain;
                    }

                    startInfo.Password = Credential.Password;
                }

            }

            string targetMessage = StringUtil.Format(ProcessResources.StartProcessTarget, startInfo.FileName, startInfo.Arguments.Trim());
            if (!ShouldProcess(targetMessage)) { return; }

            _process = Start(startInfo);

            if (PassThru.IsPresent)
            {
                if (_process != null)
                {
                    WriteObject(_process);
                }
                else
                {
                    var message = StringUtil.Format(ProcessResources.CannotStartTheProcess);
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }
            }

            _cancellationSource = new CancellationTokenSource();

            if (Wait.IsPresent)
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        ProduceNativeProcessInput(_process);
                        ConsumeAvailableNativeProcessOutput(_process, _cancellationSource.Token, blocking: true);
                        _process.WaitForExit();
                        SetLastExitCode(_process);
                    }
                    else
                    {
                        ConsumeAvailableNativeProcessOutput(_process, _cancellationSource.Token, blocking: true);
                        SetLastExitCode(_process);
                    }
                }
                else
                {
                    var message = StringUtil.Format(ProcessResources.CannotStartTheProcess);
                    ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }
            }
            else
            {
                if (_process != null)
                {
                    _process.Exited += myProcess_Exited;
                    _process.EnableRaisingEvents = true;
                    _waitHandle = new ManualResetEvent(false);
                }
            }
        }

        /// <summary>
        /// Pass parameter from pipeline to the process.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (Wait.IsPresent) return;
            if (_process != null && _cancellationSource != null)
            {
                ProduceNativeProcessInput(_process);
                ConsumeAvailableNativeProcessOutput(_process, _cancellationSource.Token, blocking: false);
            }
            else
            {
                var message = StringUtil.Format(ProcessResources.ProcessIsNotStarted);
                ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
        }

        /// <summary>
        /// Wait for the process to terminate.
        /// </summary>
        protected override void EndProcessing()
        {
            if (Wait.IsPresent || _process == null || _cancellationSource == null) return;

            ConsumeAvailableNativeProcessOutput(_process, _cancellationSource.Token, blocking: true);

            if (_waitHandle != null)
            {
                _waitHandle.WaitOne();
            }
            if (_process.HasExited)
            {
                SetLastExitCode(_process);
            }
            else
            {
                var message = StringUtil.Format(ProcessResources.ProcessIsNotTerminated);
                ErrorRecord er = new ErrorRecord(new InvalidOperationException(message), "InvalidOperationException", ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
        }

        /// <summary>
        /// Implements ^c, after creating a process.
        /// </summary>
        protected override void StopProcessing()
        {
            if (_cancellationSource != null)
            {
                _cancellationSource.Cancel();
                _cancellationSource = null;
            }
            if (_waitHandle != null)
            {
                _waitHandle.Set();
            }
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.Kill(true);
                }
                _process = null;
            }
        }

        #endregion

        #region IDisposable Overrides

        /// <summary>
        /// Dispose WaitHandle used to honor -Wait parameter.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            System.GC.SuppressFinalize(this);
        }

        private void Dispose(bool isDisposing)
        {
            if (_waitHandle != null)
            {
                _waitHandle.Dispose();
                _waitHandle = null;
            }
            try
            {
                // Dispose the process if it's already created
                if (_process != null)
                {
                    _process.Dispose();
                }
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// When Process exits the wait handle is set.
        /// </summary>
        private void myProcess_Exited(object? sender, System.EventArgs e)
        {
            if (_waitHandle != null)
            {
                _waitHandle.Set();
            }
        }

        private string ResolveFilePath(string path)
        {
            return base.GetResolvedProviderPathFromPSPath(path, out _)[0];
        }

        private static void LoadEnvironmentVariable(ProcessStartInfo startinfo, IDictionary EnvironmentVariables)
        {
            var processEnvironment = startinfo.EnvironmentVariables;
            foreach (DictionaryEntry entry in EnvironmentVariables)
            {
                var key = entry.Key.ToString();
                if (key == null) continue;
                if (processEnvironment.ContainsKey(key))
                {
                    processEnvironment.Remove(key);
                }

                if (key.Equals("PATH"))
                {
                    processEnvironment.Add(key,
                        Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Machine) + ";" +
                        Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User));
                }
                else
                {
                    processEnvironment.Add(key, entry.Value?.ToString());
                }
            }
        }

        private Process Start(ProcessStartInfo startInfo)
        {
            var process = new Process() { StartInfo = startInfo };
            SetupInputOutputRedirection(process);
            process.Start();
            return process;
        }

        private void SetupInputOutputRedirection(Process p)
        {
            p.StartInfo.RedirectStandardInput = base.MyInvocation.ExpectingInput;
            p.StartInfo.RedirectStandardOutput = !DontRedirectOutputs;
            p.StartInfo.RedirectStandardError = !DontRedirectOutputs;
        }

        /// <summary>
        /// Read the input from the pipeline and send it down the native process.
        /// </summary>
        private void ProduceNativeProcessInput(Process p)
        {
            if (!base.MyInvocation.ExpectingInput) return;
            p.StandardInput.WriteLine(InputObject);
        }

        /// <summary>
        /// Read the output from the native process and send it down the line.
        /// </summary>
        private void ConsumeAvailableNativeProcessOutput(Process p, CancellationToken ct, bool blocking)
        {
            if (DontRedirectOutputs) return;
            var _buffer = OutputBuffer ?? 256;
            var _merge = MergeStandardErrorToOutput;
            var _wrap = WrapOutputStream;
            var _task = infer(async () =>
            {
                var _out = p.StandardOutput;
                var _err = p.StandardError;
                var lstOut = _wrap ? null : mkList<String>(_buffer);
                var lstErr = _wrap ? null : mkList<String>(_buffer);
                var lstWrap = _wrap ? mkList<WrapObject>(_buffer) : null;
                string? str = null;
                Task<string?>? tout = null;
                Task<string?>? terr = null;
                do
                {
                    var count = 0;
                    do
                    {
                        tout ??= _out.ReadLineAsync();
                        terr ??= _err.ReadLineAsync();
                        str = await Task.WhenAny(tout, terr).Result;
                        if (tout.IsCompleted)
                        {
                            var r = tout.Result!;
                            if (r != null)
                            {
                                if (_buffer == 1 && _wrap) base.WriteObject(WrapObject.Output(r));
                                else if (_buffer == 1) base.WriteObject(r);
                                else if (_wrap) lstWrap!.Add(WrapObject.Output(r));
                                else lstOut!.Add(r);
                                count++;
                            }
                            tout = null;
                        }
                        if (terr.IsCompleted)
                        {
                            var r = terr.Result!;
                            if (r != null)
                            {
                                if (_buffer == 1 && _wrap) base.WriteObject(WrapObject.Error(r));
                                else if (_buffer == 1) base.WriteError(mkError(r));
                                if (_wrap) lstWrap!.Add(WrapObject.Error(r));
                                else if (_merge) lstOut!.Add(r);
                                else lstErr!.Add(r);
                                count++;
                            }
                            terr = null;
                        }
                    } while (count < _buffer && str != null);
                    if (_wrap && lstWrap!.Count > 0)
                    {
                        base.WriteObject(lstWrap);
                        lstWrap.Clear();
                    }
                    if (!_wrap && lstOut!.Count > 0)
                    {
                        base.WriteObject(lstOut);
                        lstOut.Clear();
                    }
                    if (!_wrap && lstErr!.Count > 0)
                    {
                        base.WriteError(mkError(lstErr));
                        lstErr.Clear();
                    }
                } while (str != null);
            });
            if (blocking)
            {
                AsyncContext.Run(_task, ct);
            }
            else
            {
                Task.Factory.Run(_task);
            }
        }

        private ErrorRecord mkError(string message)
        {
            return new ErrorRecord(new StdErr(message), null, ErrorCategory.FromStdErr, null);
        }
        private ErrorRecord mkError(List<string> messages)
        {
            return new ErrorRecord(new StdErr(messages), null, ErrorCategory.FromStdErr, null);
        }

        private class StdErr : Exception
        {
            private static List<string> empty = new List<string>();
            private readonly List<string> err;

            public StdErr(List<string> err) : base(ToString(err))
            {
                this.err = err;
            }

            public StdErr(string message) : base(message)
            {
                this.err = empty;
            }

            public IList<string> ErrorList => err;

            public static string ToString(List<string> err)
            {
                var sb = new StringBuilder();
                foreach (var str in err) sb.Append(str);
                return sb.ToString();
            }

        }

        private struct WrapObject
        {

            public WrapObject(RedirectionStream stream, string message)
            {
                Stream = stream;
                Message = message;
            }

            public RedirectionStream Stream { get; }
            public string Message { get; }


            internal static WrapObject Error(string message)
            {
                return new WrapObject(RedirectionStream.Error, message);
            }

            internal static WrapObject Output(string message)
            {
                return new WrapObject(RedirectionStream.Output, message);
            }

        }

        private static Func<TRes> infer<TRes>(Func<TRes> arg) { return arg; }

        private static List<T> mkList<T>(int capacity)
        {
            return capacity == int.MaxValue ? new List<T>(32) : new List<T>(capacity);
        }

        private void SetLastExitCode(Process process)
        {
            base.SessionState.PSVariable.Set("LASTEXITCODE", process.ExitCode);
        }

        #endregion
    }

}
