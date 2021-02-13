using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horarium.Repository;

namespace Horarium.InMemory
{
    public class InMemoryRepository : IJobRepository
    {
        private readonly OperationsProcessor _processor = new OperationsProcessor();

        private readonly JobsStorage _storage = new JobsStorage();

        private readonly ConcurrentDictionary<string, RecurrentJobSettings> _settingsStorage =
            new ConcurrentDictionary<string, RecurrentJobSettings>();

        public Task<JobDb> GetReadyJob(string machineName, TimeSpan obsoleteTime)
        {
            JobDb Query()
            {
                var job = _storage.FindReadyJob(obsoleteTime);
                if (job == null) return null;

                _storage.Remove(job.JobId);

                job.Status = JobStatus.Executing;
                job.ExecutedMachine = machineName;
                job.StartedExecuting = DateTime.UtcNow;
                job.CountStarted++;

                _storage.Add(job);

                return job;
            }

            return _processor.Execute(Query);
        }

        public Task AddJob(JobDb job)
        {
            return _processor.Execute(() => _storage.Add(job.Copy()));
        }

        public Task FailedJob(string jobId, Exception error)
        {
            return _processor.Execute(() =>
            {
                var job = _storage.GetById(jobId);
                if (job == null) return;

                _storage.Remove(job);

                job.Status = JobStatus.Failed;
                job.Error = error.Message + error.StackTrace;

                _storage.Add(job);
            });
        }

        public Task RemoveJob(string jobId)
        {
            return _processor.Execute(() => _storage.Remove(jobId));
        }

        public Task RepeatJob(string jobId, DateTime startAt, Exception error)
        {
            return _processor.Execute(() =>
            {
                var job = _storage.GetById(jobId);
                if (job == null) return;

                _storage.Remove(job);

                job.Status = JobStatus.RepeatJob;
                job.StartAt = startAt;
                job.Error = error.Message + error.StackTrace;

                _storage.Add(job);
            });
        }

        public Task AddRecurrentJob(JobDb job)
        {
            return _processor.Execute(() =>
            {
                var foundJob = _storage.FindRecurrentJobToUpdate(job.JobKey) ?? job.Copy();

                _storage.Remove(foundJob);

                foundJob.Cron = job.Cron;
                foundJob.StartAt = job.StartAt;

                _storage.Add(foundJob);
            });
        }

        public Task AddRecurrentJobSettings(RecurrentJobSettings settings)
        {
            _settingsStorage.AddOrUpdate(settings.JobKey, settings, (_, __) => settings);

            return Task.CompletedTask;
        }

        public Task<string> GetCronForRecurrentJob(string jobKey)
        {
            if (!_settingsStorage.TryGetValue(jobKey, out var settings))
                throw new Exception($"Settings for recurrent job (jobKey = {jobKey}) aren't found");

            return Task.FromResult(settings.Cron);
        }

        public Task<Dictionary<JobStatus, int>> GetJobStatistic()
        {
            return Task.FromResult(_storage.GetStatistics());
        }

        public Task RescheduleRecurrentJob(string jobId, DateTime? startAt, Exception error)
        {
            return _processor.Execute(() =>
            {
                var job = _storage.GetById(jobId);
                if (job == null) return;

                _storage.Remove(job);
                
                if (startAt == null)
                {
                    return;
                }

                job.Status = JobStatus.Ready;
                job.StartAt = startAt.Value;
                job.Error = error.Message + error.StackTrace;

                _storage.Add(job);
            });
        }
    }
}