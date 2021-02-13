using System;
using System.Threading.Tasks;
using Cronos;
using Horarium.Interfaces;

namespace Horarium.Builders.Recurrent
{
    internal class RecurrentJobBuilder : JobBuilder, IRecurrentJobBuilder
    {
        private readonly IAdderJobs _adderJobs;

        public RecurrentJobBuilder(IAdderJobs adderJobs, string cron, Type jobType, TimeSpan obsoleteInterval) 
            : base(jobType)
        {
            _adderJobs = adderJobs;
            Job.ObsoleteInterval = obsoleteInterval;
            
            Job.Cron = cron;
        }
        
        public IRecurrentJobBuilder WithKey(string jobKey)
        {
            Job.JobKey = jobKey;
            return this;
        }
        
        public override Task Schedule()
        {
            var nextOccurence = Utils.ParseAndGetNextOccurrence(Job.Cron);

            if (!nextOccurence.HasValue)
            {
                return Task.CompletedTask;
            }
            
            Job.StartAt = nextOccurence.Value;
            Job.JobKey = Job.JobKey ?? Job.JobType.Name;
            
            return _adderJobs.AddRecurrentJob(Job);
        }
    }
}