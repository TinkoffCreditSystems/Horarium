using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horarium.Repository
{
    public interface IJobRepository
    {
        Task<JobDb> GetReadyJob(string machineName, TimeSpan obsoleteTime);

        Task AddJob(JobDb job);

        Task FailedJob(string jobId, Exception error);

        Task RemoveJob(string jobId);

        Task RepeatJob(string jobId, DateTime startAt, Exception error);

        Task AddRecurrentJob(JobDb job);

        Task AddRecurrentJobSettings(RecurrentJobSettings settings);

        Task<string> GetCronForRecurrentJob(string jobKey);

        Task<Dictionary<JobStatus, int>> GetJobStatistic();

        Task RescheduleRecurrentJob(string jobId, DateTime? startAt, Exception error);
    }
}