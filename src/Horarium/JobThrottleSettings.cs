using System;

namespace Horarium
{
    public class JobThrottleSettings
    {
        /// <summary>
        /// When `true`, IntervalStartJob will automatically increase if there is no jobs available
        /// </summary>
        public bool UseJobThrottle { get; set; }
        
        /// <summary>
        /// After all attempts are exhausted, waiting interval is increased by formula:
        /// <c>currentInterval + (currentInterval * intervalMultiplier)</c>
        /// </summary>
        public int JobRetrievalAttempts { get; set; } = 10;
        
        /// <summary>
        /// Multiplier to get the next waiting interval
        /// </summary>
        public double IntervalMultiplier { get; set; } = 0.25;
        
        /// <summary>
        /// Maximum waiting interval
        /// </summary>
        public TimeSpan MaxJobThrottleInterval { get; set; } = TimeSpan.FromSeconds(30);
    }
}