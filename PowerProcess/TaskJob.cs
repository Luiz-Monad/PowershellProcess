// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// This code was part of PSThreadJob.

using PowerProcess.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace PowerProcess
{

    /// <summary>
    /// JobSourceAdapter
    /// </summary>
    public sealed class TaskJobSourceAdapter : JobSourceAdapter
    {
        #region Members

        private readonly ConcurrentDictionary<Guid, Job2> _repository;

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor
        /// </summary>
        public TaskJobSourceAdapter()
        {
            Name = nameof(TaskJobSourceAdapter);
            _repository = new ConcurrentDictionary<Guid, Job2>();
        }

        #endregion

        #region JobSourceAdapter Implementation

        /// <summary>
        /// NewJob
        /// </summary>
        public override Job2? NewJob(JobInvocationInfo specification)
        {
            var job = specification.Parameters[0][0].Value as TaskJob;
            if (job != null)
            {
                _repository.TryAdd(job.InstanceId, job);
            }
            return job;
        }

        /// <summary>
        /// GetJobs
        /// </summary>
        public override IList<Job2> GetJobs()
        {
            return _repository.Values.ToArray();
        }

        /// <summary>
        /// GetJobsByName
        /// </summary>
        public override IList<Job2> GetJobsByName(string name, bool recurse)
        {
            var rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobsByCommand
        /// </summary>
        public override IList<Job2> GetJobsByCommand(string command, bool recurse)
        {
            var rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.Command.Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobByInstanceId
        /// </summary>
        public override Job2? GetJobByInstanceId(Guid instanceId, bool recurse)
        {
            if (_repository.TryGetValue(instanceId, out var job))
            {
                return job;
            }
            return null;
        }

        /// <summary>
        /// GetJobBySessionId
        /// </summary>
        public override Job2? GetJobBySessionId(int id, bool recurse)
        {
            foreach (var job in _repository.Values)
            {
                if (job.Id == id)
                {
                    return job;
                }
            }
            return null;
        }

        /// <summary>
        /// GetJobsByState
        /// </summary>
        public override IList<Job2> GetJobsByState(JobState state, bool recurse)
        {
            var rtnList = new List<Job2>();
            foreach (var job in _repository.Values)
            {
                if (job.JobStateInfo.State == state)
                {
                    rtnList.Add(job);
                }
            }
            return rtnList;
        }

        /// <summary>
        /// GetJobsByFilter
        /// </summary>
        public override IList<Job2> GetJobsByFilter(Dictionary<string, object> filter, bool recurse)
        {
            throw new PSNotSupportedException();
        }

        /// <summary>
        /// RemoveJob
        /// </summary>
        public override void RemoveJob(Job2 job)
        {
            if (_repository.TryGetValue(job.InstanceId, out var removeJob))
            {
                removeJob.StopJob();
                _repository.TryRemove(job.InstanceId, out _);
            }
        }

        #endregion
    }

    /// <summary>
    /// Job
    /// </summary>
    public sealed class TaskJob : Job2
    {
        #region Private members

        private readonly Action<TaskJob> _runTask;
        private readonly CancellationTokenSource _cancelSource;

        #endregion

        #region Properties

        /// <summary>
        /// Specifies the job definition for the JobManager
        /// </summary>
        public JobDefinition TaskJobDefinition
        {
            get;
            private set;
        }

        #endregion

        #region Constructors

#pragma warning disable CS8618
        private TaskJob() { }
#pragma warning restore CS8618

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="runTask"></param>
        /// <param name="cancelSource"></param>
        public TaskJob(
            PSCmdlet psCmdlet,
            string? name,
            Action<TaskJob> runTask,
            CancellationTokenSource cancelSource)
            : base(String.Empty, name)
        {
            _runTask = runTask;
            _cancelSource = cancelSource;

            PSJobTypeName = "TaskJob"
                ;
            // Hook up data streams.
            Output = new PSDataCollection<PSObject>
            {
                EnumeratorNeverBlocks = true
            };

            Error = new PSDataCollection<ErrorRecord>
            {
                EnumeratorNeverBlocks = true
            };

            Progress = new PSDataCollection<ProgressRecord>
            {
                EnumeratorNeverBlocks = true
            };

            Verbose = new PSDataCollection<VerboseRecord>
            {
                EnumeratorNeverBlocks = true
            };

            Warning = new PSDataCollection<WarningRecord>
            {
                EnumeratorNeverBlocks = true
            };

            Debug = new PSDataCollection<DebugRecord>
            {
                EnumeratorNeverBlocks = true
            };

            Information = new PSDataCollection<InformationRecord>
            {
                EnumeratorNeverBlocks = true
            };

            // Create the JobManager job definition and job specification, and add to the JobManager.
            TaskJobDefinition = new JobDefinition(typeof(TaskJobSourceAdapter), "", Name);
            var parameterCollection = new Dictionary<string, object> {
                { nameof(psCmdlet.JobManager.NewJob), this }
            };
            var jobSpecification = new JobInvocationInfo(TaskJobDefinition, parameterCollection);
            var newJob = psCmdlet.JobManager.NewJob(jobSpecification);
            System.Diagnostics.Debug.Assert(newJob == this, "JobManager must return this job");
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cancelSource.Cancel();
                Output.Complete();
                Error.Complete();
                Progress.Complete();
                Verbose.Complete();
                Warning.Complete();
                Debug.Complete();
                Information.Complete();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// StatusMessage
        /// </summary>
        public override string StatusMessage
        {
            get { return string.Empty; }
        }

        /// <summary>
        /// HasMoreData
        /// </summary>
        public override bool HasMoreData
        {
            get
            {
                return (Output.Count > 0 ||
                        Error.Count > 0 ||
                        Progress.Count > 0 ||
                        Verbose.Count > 0 ||
                        Debug.Count > 0 ||
                        Warning.Count > 0);
            }
        }

        /// <summary>
        /// Location
        /// </summary>
        public override string Location
        {
            get { return "PowerShell"; }
        }

        /// <summary>
        /// ReportError
        /// </summary>
        /// <param name="e"></param>
        public void ReportError(Exception e)
        {
            try
            {
                SetJobState(JobState.Failed);

                Error.Add(
                        new ErrorRecord(e, "ThreadJobError", ErrorCategory.InvalidOperation, this));
            }
            catch (ObjectDisposedException)
            {
                // Ignore. Thrown if Job is disposed (race condition.).
            }
            catch (PSInvalidOperationException)
            {
                // Ignore. Thrown if Error collection is closed (race condition.).
            }
        }

        #endregion

        #region Base class overrides

        /// <summary>
        /// StartJob
        /// </summary>
        public override void StartJob()
        {
            if (JobStateInfo.State != JobState.NotStarted)
            {
                throw new Exception(PSThreadJobResources.CannotStartJob);
            }
            try
            {
                Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = $"TaskJob ${Name}";
                    try
                    {
                        SetJobState(JobState.Running);
                        _runTask(this);
                        SetJobState(JobState.Completed);
                    }
                    catch (Exception e)
                    {
                        ReportError(e);
                    }
                }, _cancelSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception e)
            {
                ReportError(e);
            }
        }

        /// <summary>
        /// StartJobAsync
        /// </summary>
        public override void StartJobAsync()
        {
            StartJob();
            OnStartJobCompleted(
                new AsyncCompletedEventArgs(null, false, this));
        }

        /// <summary>
        /// OnStartJobCompleted
        /// </summary>
        /// <param name="eventArgs"></param>
        protected override void OnStartJobCompleted(AsyncCompletedEventArgs eventArgs)
        {
            base.OnStartJobCompleted(eventArgs);
        }

        /// <summary>
        /// StopJob
        /// </summary>
        public override void StopJob()
        {
            if (JobStateInfo.State == JobState.Failed) return;
            if (JobStateInfo.State != JobState.Running)
            {
                throw new Exception(PSThreadJobResources.CannotStopJob);
            }
            try
            {
                _cancelSource.Cancel();
            }
            catch (Exception e)
            {
                ReportError(e);
            }
        }

        /// <summary>
        /// StopJob
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void StopJob(bool force, string reason)
        {
            StopJob();
        }

        /// <summary>
        /// StopJobAsync
        /// </summary>
        public override void StopJobAsync()
        {
            StartJob();
            OnStopJobCompleted(
                new AsyncCompletedEventArgs(null, false, this));
        }

        /// <summary>
        /// StopJobAsync
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void StopJobAsync(bool force, string reason)
        {
            StopJobAsync();
        }

        /// <summary>
        /// OnStopJobCompleted
        /// </summary>
        /// <param name="eventArgs"></param>
        protected override void OnStopJobCompleted(AsyncCompletedEventArgs eventArgs)
        {
            base.OnStopJobCompleted(eventArgs);
        }

        #region Not implemented

        /// <summary>
        /// SuspendJob
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void SuspendJob(bool force, string reason)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJob
        /// </summary>
        public override void SuspendJob()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJobAsync
        /// </summary>
        public override void SuspendJobAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// SuspendJobAsync
        /// </summary>
        /// <param name="force"></param>
        /// <param name="reason"></param>
        public override void SuspendJobAsync(bool force, string reason)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ResumeJob
        /// </summary>
        public override void ResumeJob()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ResumeJobAsync
        /// </summary>
        public override void ResumeJobAsync()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// UnblockJob
        /// </summary>
        public override void UnblockJob()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// UnblockJobAsync
        /// </summary>
        public override void UnblockJobAsync()
        {
            throw new NotImplementedException();
        }

        #endregion

        #endregion

    }

}
