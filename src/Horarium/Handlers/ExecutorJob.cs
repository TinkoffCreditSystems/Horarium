using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Horarium.Interfaces;
using Horarium.Repository;
using Newtonsoft.Json;
using Horarium.Builders.Recurrent;

namespace Horarium.Handlers
{
    public class ExecutorJob : IExecutorJob
    {
        private readonly IJobRepository _jobRepository;
        private readonly IAdderJobs _adderJobs;
        private readonly HorariumSettings _settings;

        public ExecutorJob(
            IJobRepository jobRepository,
            IAdderJobs adderJobs,
            HorariumSettings settings)
        {
            _jobRepository = jobRepository;
            _adderJobs = adderJobs;
            _settings = settings;
        }

        public Task Execute(JobMetadata jobMetadata)
        {
            if (jobMetadata.JobType.GetTypeInfo().GetInterfaces().Contains(typeof(IJobRecurrent)))
            {
                return ExecuteJobRecurrent(jobMetadata);
            }

            return ExecuteJob(jobMetadata);
        }

        private async Task ExecuteJob(JobMetadata jobMetadata)
        {
            dynamic jobImplementation = null;

            try
            {
                using (var scope = _settings.JobScopeFactory.Create())
                {
                    try
                    {
                        jobImplementation = scope.CreateJob(jobMetadata.JobType);
                    }
                    catch (Exception ex)
                    {
                        //Дополнительное логирование ошибки, когда джоб не может быть создан
                        _settings.Logger.Error($"Ошибка создания джоба {jobMetadata.JobType}", ex);
                        throw;
                    }

                    _settings.Logger.Debug("got jobImplementation -" + jobImplementation.GetType());
                    _settings.Logger.Debug("got JobParam -" + jobMetadata.JobParam.GetType());
                    await jobImplementation.Execute((dynamic) jobMetadata.JobParam);

                    _settings.Logger.Debug("jobMetadata excecuted");

                    if (jobMetadata.NextJob != null)
                    {
                        jobMetadata.NextJob.StartAt = DateTime.UtcNow + jobMetadata.NextJob.Delay.GetValueOrDefault();

                        await _jobRepository.AddJob(JobDb.CreatedJobDb(jobMetadata.NextJob,
                            _settings.JsonSerializerSettings));

                        _settings.Logger.Debug("next jobMetadata added");
                    }

                    await _jobRepository.RemoveJob(jobMetadata.JobId);

                    _settings.Logger.Debug("jobMetadata saved success");
                }
            }
            catch (Exception ex)
            {
                await HandleFailed(jobMetadata, ex, jobImplementation);
            }
        }

        private async Task ExecuteJobRecurrent(JobMetadata jobMetadata)
        {
            try
            {
                using (var scope = _settings.JobScopeFactory.Create())
                {
                    dynamic jobImplementation;
                    try
                    {
                        jobImplementation = scope.CreateJob(jobMetadata.JobType);
                    }
                    catch (Exception ex)
                    {
                        //Дополнительное логирование ошибки, когда джоб не может быть создан
                        _settings.Logger.Error($"Ошибка создания джоба {jobMetadata.JobType}", ex);
                        throw;
                    }

                    _settings.Logger.Debug("got jobImplementation -" + jobImplementation.GetType());

                    await jobImplementation.Execute();

                    _settings.Logger.Debug("jobMetadata excecuted");

                    await _jobRepository.RescheduleRecurrentJob(jobMetadata.JobId,
                        Utils.ParseAndGetNextOccurrence(jobMetadata.Cron), null);

                    _settings.Logger.Debug("jobMetadata saved success");
                }
            }
            catch (Exception ex)
            {
                await _jobRepository.RescheduleRecurrentJob(jobMetadata.JobId,
                    Utils.ParseAndGetNextOccurrence(jobMetadata.Cron), ex);
            }
            finally
            {
                await ScheduleRecurrentNextTime(jobMetadata);
            }
        }

        private async Task HandleFailed(JobMetadata jobMetadata, Exception ex, dynamic jobImplementation)
        {
            _settings.Logger.Debug(ex);

            var maxRepeatCount =
                jobMetadata.MaxRepeatCount != 0 ? jobMetadata.MaxRepeatCount : _settings.MaxRepeatCount;

            if (jobMetadata.CountStarted >= maxRepeatCount)
            {
                if (jobImplementation != null && jobImplementation is IAllRepeatesIsFailed)
                {
                    try
                    {
                        await jobImplementation.FailedEvent((dynamic) jobMetadata.JobParam, ex);
                    }
                    catch (Exception failedEventException)
                    {
                        _settings.Logger.Error($"{jobMetadata.JobType}'s .FailedEvent threw", failedEventException);
                    }
                }

                await _jobRepository.FailedJob(jobMetadata.JobId, ex);
                _settings.Logger.Debug("jobMetadata saved failed");
            }
            else
            {
                await _jobRepository.RepeatJob(jobMetadata.JobId, GetNextStartFailedJobTime(jobMetadata), ex);
                _settings.Logger.Debug("jobMetadata saved repeat");
            }
        }

        private DateTime GetNextStartFailedJobTime(JobMetadata jobMetadata)
        {
            IFailedRepeatStrategy strategy;

            if (jobMetadata.RepeatStrategy != null)
            {
                strategy = (IFailedRepeatStrategy) Activator.CreateInstance(jobMetadata.RepeatStrategy);
            }
            else
            {
                strategy = _settings.FailedRepeatStrategy;
            }

            return DateTime.UtcNow + strategy.GetNextStartInterval(jobMetadata.CountStarted);
        }

        private async Task ScheduleRecurrentNextTime(JobMetadata metadata)
        {
            var cron = await _jobRepository.GetCronForRecurrentJob(metadata.JobKey);

            await new RecurrentJobBuilder(_adderJobs, cron, metadata.JobType, metadata.ObsoleteInterval)
                .WithKey(metadata.JobKey)
                .Schedule();
        }
    }
}