using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;

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
    [Cmdlet(VerbsLifecycle.Invoke, "ProcessFast", SupportsShouldProcess = true, HelpUri = "https://go.microsoft.com/fwlink/?LinkID=2097141")]
    [OutputType(typeof(Process))]
    public sealed class InvokeProcessFastCommand : PSCmdlet, IDisposable
    {
        private Process? _process = null;
        private ManualResetEvent? _waitHandle = null;
        private CancellationTokenSource? _cancellationSource = null;

        #region Parameters
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private const string DefaultParameterSet = "ScriptBlock";
        private const string WinEnvParameterSet = "WinEnv";

        /// <summary>
        /// Path/FileName of the process to start.
        /// </summary>
        [Parameter(ParameterSetName = DefaultParameterSet, Mandatory = true, Position = 0)]
        [ValidateNotNullOrEmpty]
        [Alias("PSPath", "Path")]
        public string FilePath { get; set; }

        /// <summary>
        /// Arguments for the process.
        /// </summary>
        [Parameter(ParameterSetName = DefaultParameterSet, Position = 1)]
        [Alias("Args")]
        public string[]? ArgumentList { get; set; }

        /// <summary>
        /// Credentials for the process.
        /// </summary>
        [Parameter(ParameterSetName = WinEnvParameterSet)]
        [Alias("RunAs")]
        [ValidateNotNullOrEmpty]
        [Credential]
        public PSCredential? Credential { get; set; }

        /// <summary>
        /// Working directory of the process.
        /// </summary>
        [Parameter(ParameterSetName = DefaultParameterSet)]
        [ValidateNotNullOrEmpty]
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Load user profile from registry.
        /// </summary>
        [Parameter(ParameterSetName = WinEnvParameterSet)]
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
        [Parameter(ParameterSetName = WinEnvParameterSet)]
        public SwitchParameter UseNewEnvironment { get; set; }

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        #endregion

        #region Pipeline

        [Parameter(ValueFromPipeline = true)]
        public string? InputObject { get; set; }

        /// <summary>
        /// Buffer the output stream.
        /// </summary>
        [Parameter]
        public int? OutputBuffer { get; set; }

        #endregion

        #region Overrides

        /// <summary>
        /// BeginProcessing.
        /// </summary>
        protected override void BeginProcessing()
        {

            ProcessStartInfo startInfo = new();

            // Path = Mandatory parameter -> Will not be empty.
            try
            {
                var cmdinfo = base.InvokeCommand.GetCommand(
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
                    var message = StringUtil.Format(ProcessResources.InvalidInput, nameof(WorkingDirectory));
                    var er = new ErrorRecord(new DirectoryNotFoundException(message), nameof(DirectoryNotFoundException), ErrorCategory.InvalidOperation, null);
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

            if (this.ParameterSetName.Equals(WinEnvParameterSet))
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
#pragma warning disable CA1416 // Validate platform compatibility
                startInfo.LoadUserProfile = LoadUserProfile;
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
#pragma warning restore CA1416 // Validate platform compatibility
#endif
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
                    var er = new ErrorRecord(new InvalidOperationException(message), nameof(InvalidOperationException), ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                    return;
                }
            }

            _cancellationSource = new CancellationTokenSource();

            if (!Wait.IsPresent)
            {
                return;
            }

            if (_process != null)
            {
                if (_process.HasExited)
                {
                    ConsumeAvailableNativeProcessOutput(blocking: true, _process, _cancellationSource.Token);
                    SetLastExitCode(_process);
                    _process = null;
                }
                else
                {
                    _process.Exited += myProcess_Exited;
                    _process.EnableRaisingEvents = true;
                    _waitHandle = new ManualResetEvent(false);
                }
            }
            else
            {
                var message = StringUtil.Format(ProcessResources.CannotStartTheProcess);
                var er = new ErrorRecord(new InvalidOperationException(message), nameof(InvalidOperationException), ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
        }

        /// <summary>
        /// Pass parameter from pipeline to the process.
        /// </summary>
        protected override void ProcessRecord()
        {
            if (!base.MyInvocation.ExpectingInput) return;
            if (_process != null && _cancellationSource != null)
            {
                ProduceNativeProcessInput(_process);
            }
            else
            {
                var message = StringUtil.Format(ProcessResources.ProcessIsNotStarted);
                var er = new ErrorRecord(new InvalidOperationException(message), nameof(InvalidOperationException), ErrorCategory.InvalidOperation, null);
                ThrowTerminatingError(er);
            }
        }

        /// <summary>
        /// Wait for the process to terminate.
        /// </summary>
        protected override void EndProcessing()
        {
            if (_process == null || _cancellationSource == null) return;

            if (Wait.IsPresent && _waitHandle != null)
            {
                ConsumeAvailableNativeProcessOutput(blocking: true, _process, _cancellationSource.Token);

                _waitHandle.WaitOne();

                if (_process.HasExited)
                {
                    SetLastExitCode(_process);
                }
                else
                {
                    var message = StringUtil.Format(ProcessResources.ProcessIsNotTerminated);
                    var er = new ErrorRecord(new InvalidOperationException(message), nameof(InvalidOperationException), ErrorCategory.InvalidOperation, null);
                    ThrowTerminatingError(er);
                }
            }
            else
            {
                var p = _process;
                _process = null; //suppress finalize
                ConsumeAvailableNativeProcessOutput(blocking: false, p, _cancellationSource.Token);
                SetLastExitCode(0);
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
            p.StandardInput.WriteLine(InputObject);
        }

        /// <summary>
        /// Read the output from the native process and send it down the line.
        /// </summary>
        private void ConsumeAvailableNativeProcessOutput(bool blocking, Process p, CancellationToken ct)
        {
            if (DontRedirectOutputs) return;
            var _buffer = OutputBuffer ?? 256;
            var _merge = MergeStandardErrorToOutput.ToBool();
            var _wrap = WrapOutputStream.ToBool();
            var _sing = _buffer == 1;
            var _task = infer(async () =>
            {
                var _out = p.StandardOutput;
                var _err = p.StandardError;

                var streams = new[] { _out, _err };
                var tasks = new Task<string?>?[2] { null, null };

                // calculate redirect
                var src = new[]
                {
                   _wrap ? WrapSource.Out : (_sing ? WrapSource.PSO : WrapSource.Str),
                   _wrap ? WrapSource.Err : (_sing ? WrapSource.MKE : WrapSource.Str),
                };
                var tgt = new[]
                {
                   _sing ? RedirTarget.Out : RedirTarget.Lst,
                   _sing ? RedirTarget.Err : RedirTarget.Lst,
                };
                var lst_tgt = new[]
                {
                   RedirTarget.Out,
                   RedirTarget.Err,
                };
                var wlst = _wrap ? NewList<Object>(_buffer) : null;
                var lst = new[]
                {
                    _sing ? null : (_wrap ? (IList) wlst! : NewList<String>(_buffer)),
                    _sing ? null : (_wrap ? (IList) wlst! : NewList<String>(_buffer)),
                };
                if (_merge)
                {
                    tgt[1] = tgt[0];
                    lst[1] = lst[0];
                    lst_tgt[1] = lst_tgt[0];
                }

                // stream
                string? str = null;
                do
                {
                    var count = 0;
                    do
                    {
                        // null if complete, fire another one
                        for (var i = 0; i < streams.Length; i++)
                        {
                            tasks[i] ??= streams[i].ReadLineAsync();
                        }
                        // wait
                        str = await Task.WhenAny(tasks!).Result;
                        // for each stream
                        for (var i = 0; i < streams.Length; i++)
                        {
                            // get and clear for next read if completed
                            var t = tasks[i]!;
                            if (!t.IsCompleted) continue;
                            tasks[i] = null;

                            // check for end of stream
                            var r = t.Result!;
                            if (r == null) continue;

                            // now redirect
                            RedirectMessage(r, src[i], tgt[i], lst[i]);
                            count++;
                        }
                        // until buffer full or end of any streams
                    } while (count < _buffer && str != null);

                    // send collected results for each stream respectively
                    for (var i = 0; i < streams.Length; i++)
                    {
                        if (lst[i] == null) continue;
                        RedirectList(lst[i]!, lst_tgt[i]);
                    }

                    // until end of all streams
                } while (str != null);
            });
            if (blocking)
            {
                AsyncContext.Run(_task, ct);
            }
            else
            {
                var cts = new CancellationTokenSource();
                var job = new TaskJob(
                    this,
                    p.ProcessName,
                    () => AsyncContext.Run(_task, cts.Token),
                    cts);
                cts.Token.Register(() =>
                {
                    if (!p.HasExited)
                    {
                        p.Kill(true);
                    }
                    p.Dispose();
                });
                job.StartJobAsync();
            }
        }

        private void RedirectMessage(
            string message,
            WrapSource source,
            RedirTarget target,
            IList? lst)
        {
            var m = message;
            object? o = null;
            switch (source)
            {
                case WrapSource.Str: o = m; break;
                case WrapSource.Out: o = WrapObject.Output(m); break;
                case WrapSource.Err: o = WrapObject.Error(m); break;
                case WrapSource.PSO: o = PSObject.AsPSObject(m); break;
                case WrapSource.MKE: o = MakeError(m); break;
            }
            switch (target)
            {
                case RedirTarget.Out: base.WriteObject(o); break;
                case RedirTarget.Err: base.WriteError(MakeError(m)); break;
                case RedirTarget.Lst: lst!.Add(o!); break;
            }
        }

        private void RedirectList(
            object lst,
            RedirTarget target)
        {
            var l = (IList)lst;
            if (l.Count == 0) return;
            switch (target)
            {
                case RedirTarget.Out: base.WriteObject(PSObject.AsPSObject(lst)); break;
                case RedirTarget.Err: base.WriteError(MakeError((List<String>)lst)); break;
                case RedirTarget.Lst: throw new InvalidOperationException();
            }
            l.Clear();
        }

        private enum WrapSource
        {
            Str = 0,
            Out = 1,
            Err = 2,
            PSO = 3,
            MKE = 4,
        }

        private enum RedirTarget
        {
            Lst = 0,
            Out = 1,
            Err = 2,
        }

        #endregion

        #region Helpers

        private static ErrorRecord MakeError(string message)
        {
            return new ErrorRecord(new StdErr(message), null, ErrorCategory.FromStdErr, null);
        }

        private static ErrorRecord MakeError(List<string> messages)
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

        public struct WrapObject
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

        private static List<T> NewList<T>(int capacity)
        {
            return capacity == int.MaxValue ? new List<T>(32) : new List<T>(capacity);
        }

        private void SetLastExitCode(Process process)
        {
            SetLastExitCode(process.ExitCode);
        }

        private void SetLastExitCode(int exitCode)
        {
            base.SessionState.PSVariable.Set("LASTEXITCODE", exitCode);
        }

        #endregion

    }

}
