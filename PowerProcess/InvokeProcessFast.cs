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
            Func<TaskJob?, PSCmdlet, Func<Task<bool>>> _task = ((_job, _cmdlet) => () =>
                   ConsumeAvailableNativeProcessOutputAsync(
                       _process: p,
                       _cmdlet: this,
                       _job: _job,
                       _merge: _merge,
                       _wrap: _wrap,
                       _buffer: _buffer));
            if (blocking)
            {
                AsyncContext.Run(_task(null, this), ct);
            }
            else
            {
                var cts = new CancellationTokenSource();
                var job = new TaskJob(
                    this,
                    p.ProcessName,
                    job => AsyncContext.Run(_task(job, this), cts.Token),
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

        private static async Task<bool> ConsumeAvailableNativeProcessOutputAsync(
            Process _process,
            PSCmdlet _cmdlet,
            TaskJob? _job,
            bool _merge,
            bool _wrap,
            int _buffer)
        {
            var _out = _process.StandardOutput;
            var _err = _process.StandardError;

            var streams = new[] { _out, _err };
            var ids = new List<int>(2) { 0, 1 };
            var tasks = new List<Task<string?>?>(2);

            // calculate redirect
            CalculareRedirect(
                _job != null,
                _merge,
                _wrap,
                _buffer,
                out var src,
                out var buf_tgt,
                out var buf_stream,
                out var tgt);

            // stream from source to target
            foreach (var strm in streams)
            {
                tasks.Add(strm.ReadLineAsync());
            }
            do
            {
                var count = 0;
                do
                {
                    // wait
                    var t = await Task.WhenAny(tasks!);

                    // find which stream
                    var i = tasks.IndexOf(t);
                    if (!t.IsCompleted) continue;

                    // check for end of stream
                    var r = t.Result!;
                    if (r == null)
                    {
                        tasks.RemoveAt(i);
                        ids.RemoveAt(i);
                        continue;
                    }

                    // get and clear for next read if completed
                    var id = ids[i];
                    tasks[i] = streams[id].ReadLineAsync();

                    // now redirect
                    var o = WrapMessage(r, src[id]);
                    RedirectMessage(o, buf_tgt[id], buf_stream[id], _job, _cmdlet);
                    count++;

                    // until buffer full or end of any streams
                } while (count < _buffer && tasks.Count > 0);

                // send collected results for each stream respectively
                for (var i = 0; i < streams.Length; i++)
                {
                    RedirectList(buf_stream[i], tgt[i], _job, _cmdlet);
                }

                // until end of all streams
            } while (tasks.Count > 0);

            return true;
        }

        private static void CalculareRedirect(
            bool task, bool merge, bool wrap, int buffer,
            out WrapSource[] wrapSrc,
            out RedirTarget[] redirTgt, out object?[] redirStream,
            out FinalTarget[] finalTgt)
        {
            if (!wrap)
            {
                if (buffer != 1)
                {
                    wrapSrc = new[] { WrapSource.Str, WrapSource.Str };
                    redirTgt = new[] { RedirTarget.StrLst, RedirTarget.StrLst };
                    redirStream = new[] { NewList<string>(buffer), NewList<string>(buffer) };
                    finalTgt = new[] { FinalTarget.OutStrLst, FinalTarget.ErrStrLst };
                }
                else
                {
                    wrapSrc = new[] { WrapSource.Pso, WrapSource.Rcd };
                    redirTgt = new[] { RedirTarget.Out, RedirTarget.Err };
                    redirStream = new[] { (object?)null, (object?)null };
                    finalTgt = new[] { FinalTarget.Nop, FinalTarget.Nop };
                }
            }
            else
            {
                if (buffer != 1)
                {
                    wrapSrc = new[] { WrapSource.Out, WrapSource.Err };
                    redirTgt = new[] { RedirTarget.ObjLst, RedirTarget.ObjLst };
                    redirStream = new[] { NewList<object>(buffer), NewList<object>(buffer) };
                    finalTgt = new[] { FinalTarget.OutObjLst, FinalTarget.ErrObjLst };
                }
                else
                {
                    wrapSrc = new[] { WrapSource.Out, WrapSource.Wse };
                    redirTgt = new[] { RedirTarget.Out, RedirTarget.Err };
                    redirStream = new[] { (object?)null, (object?)null };
                    finalTgt = new[] { FinalTarget.Nop, FinalTarget.Nop };
                }
            }
            if (merge)
            {
                if (wrapSrc[1] == WrapSource.Wse) wrapSrc[1] = WrapSource.Err;
                redirTgt[1] = redirTgt[0];
                redirStream[1] = redirStream[0];
                finalTgt[1] = finalTgt[0];
            }
            if (task)
            {
                wrapSrc[0] = WrapTaskStream(wrapSrc[0]);
                wrapSrc[1] = WrapTaskStream(wrapSrc[1]);
            }
        }

        private static WrapSource WrapTaskStream(
            WrapSource source)
        {
            return source switch
            {
                WrapSource.Str => WrapSource.Pso,
                WrapSource.Out => WrapSource.Wso,
                WrapSource.Err => WrapSource.Wse,
                _ => source
            };
        }

        private static object WrapMessage(
            string message,
            WrapSource source)
        {
            var m = message;
            return source switch
            {
                WrapSource.Str => m,
                WrapSource.Out => WrapObject.Output(m),
                WrapSource.Err => WrapObject.Error(m),
                WrapSource.Pso => PSObject.AsPSObject(m),
                WrapSource.Rcd => MakeError(m),
                WrapSource.Wso => MakeError(WrapObject.Output(m)),
                WrapSource.Wse => MakeError(WrapObject.Error(m)),
                _ => throw new InvalidOperationException(),
            };
        }

        private static void RedirectMessage(
            object message,
            RedirTarget target,
            object? stream,
            TaskJob? job,
            PSCmdlet cmdlet)
        {
            switch (target)
            {
                case RedirTarget.Out:
                    if (job != null) job.Output.Add((PSObject)message);
                    else cmdlet.WriteObject(message);
                    break;

                case RedirTarget.Err:
                    if (job != null) job.Error.Add((ErrorRecord)message);
                    else cmdlet.WriteError((ErrorRecord)message);
                    break;

                case RedirTarget.StrLst:
                    ((List<string>)stream!).Add((string)message);
                    break;

                case RedirTarget.ObjLst:
                    ((List<object>)stream!).Add(message);
                    break;
            }
        }

        private static void RedirectList(
            object? stream,
            FinalTarget target,
            TaskJob? job,
            PSCmdlet cmdlet)
        {
            var l = stream as IList;
            var lst = target switch
            {
                FinalTarget.OutObjLst => ((List<string>)stream!).ToArray(),
                FinalTarget.ErrObjLst => ((List<string>)stream!).ToArray(),
                FinalTarget.OutStrLst => ((List<object>)stream!).ToArray(),
                FinalTarget.ErrStrLst => ((List<object>)stream!).ToArray(),
                _ => throw new InvalidOperationException(),
            };
            if (lst == null || lst.Length == 0) return;
            switch (target)
            {
                case FinalTarget.OutObjLst:
                case FinalTarget.OutStrLst:
                    if (job != null) job.Output.Add(PSObject.AsPSObject(lst));
                    else cmdlet.WriteObject(PSObject.AsPSObject(lst));
                    break;

                case FinalTarget.ErrStrLst:
                case FinalTarget.ErrObjLst:
                    if (job != null) job.Error.Add(MakeError(lst));
                    else cmdlet.WriteError(MakeError(lst));
                    break;
            }
            l!.Clear();
        }

        private enum WrapSource
        {
            Str,
            Out,
            Err,
            Pso,
            Rcd,
            Wso,
            Wse,
        }

        private enum RedirTarget
        {
            Out,
            Err,
            StrLst,
            ObjLst,
        }

        private enum FinalTarget
        {
            Nop,
            OutStrLst,
            OutObjLst,
            ErrStrLst,
            ErrObjLst,
        }

        #endregion

        #region Helpers

        private static ErrorRecord MakeError(string message)
        {
            return new ErrorRecord(new StdErr(message), null, ErrorCategory.FromStdErr, null);
        }

        private static ErrorRecord MakeError(WrapObject wrapped)
        {
            return new ErrorRecord(new StdErr(wrapped), null, ErrorCategory.FromStdErr, null);
        }

        private static ErrorRecord MakeError(object messages)
        {
            return new ErrorRecord(new StdErr(messages), null, ErrorCategory.FromStdErr, null);
        }

        private class StdErr : Exception
        {
            private static readonly string[] empty = Array.Empty<string>();
            private readonly string[] err;

            public StdErr(object lst) : this(ConvertList(lst))
            {
            }

            public StdErr(string[] err) : base(ToString(err))
            {
                this.err = err;
            }

            public StdErr(WrapObject wrapped) : base(wrapped.Message)
            {
                this.err = empty;
            }

            public StdErr(string message) : base(message)
            {
                this.err = empty;
            }

            public IList<string> ErrorList => err;

            public static string[] ConvertList(object lst)
            {
                if (lst is string[] s)
                    return s;
                if (lst is object[] o)
                    return Array.ConvertAll(o, i => i.ToString()!);
                return empty;
            }

            public static string ToString(string[] lst)
            {
                var sb = new StringBuilder();
                foreach (var str in lst) sb.Append(str);
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

            public override string ToString()
            {
                return $"{Stream}: {Message}";
            }
        }

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
