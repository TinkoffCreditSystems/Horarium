using System;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using Horarium.Handlers;
using Horarium.Interfaces;
using Horarium.Repository;
using Xunit;

namespace Horarium.Test
{
    public class RunnerJobTest
    {
        [Fact]
        public async Task Start_Stop()
        {
            // Arrange
            var jobRepositoryMock = new Mock<IJobRepository>();

            jobRepositoryMock.Setup(x => x.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()));

            var runnerJobs = new RunnerJobs(jobRepositoryMock.Object,
                new HorariumSettings(),
                new JsonSerializerSettings(),
                Mock.Of<IHorariumLogger>(),
                Mock.Of<IExecutorJob>());

            // Act
            runnerJobs.Start();

            await Task.Delay(TimeSpan.FromSeconds(1));

            await runnerJobs.Stop();

            jobRepositoryMock.Invocations.Clear();

            await Task.Delay(TimeSpan.FromSeconds(1));

            // Assert
            jobRepositoryMock.Verify(x => x.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Fact]
        public async Task Start_RecoverAfterIntervalTimeout_AfterFailedDB()
        {
            // Arrange
            var jobRepositoryMock = new Mock<IJobRepository>();

            var settings = new HorariumSettings
            {
                IntervalStartJob = TimeSpan.FromSeconds(2),
            };

            var runnerJobs = new RunnerJobs(jobRepositoryMock.Object,
                settings,
                new JsonSerializerSettings(),
                Mock.Of<IHorariumLogger>(),
                Mock.Of<IExecutorJob>());

            jobRepositoryMock.SetupSequence(x => x.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ThrowsAsync(new Exception())
                .ReturnsAsync(new JobDb());

            // Act
            runnerJobs.Start();
            await Task.Delay(settings.IntervalStartJob + TimeSpan.FromMilliseconds(500));

            // Assert
            jobRepositoryMock.Verify(r => r.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.AtLeast(2));
        }

        [Fact]
        public async Task Start_WontRecoverBeforeIntervalTimeout_AfterFailedDB()
        {
            // Arrange
            var jobRepositoryMock = new Mock<IJobRepository>();

            var settings = new HorariumSettings
            {
                IntervalStartJob = TimeSpan.FromSeconds(2),
            };

            var runnerJobs = new RunnerJobs(jobRepositoryMock.Object,
                settings,
                new JsonSerializerSettings(),
                Mock.Of<IHorariumLogger>(),
                Mock.Of<IExecutorJob>());

            jobRepositoryMock.SetupSequence(x => x.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .ThrowsAsync(new Exception())
                .ReturnsAsync(new JobDb());

            // Act
            runnerJobs.Start();
            await Task.Delay(settings.IntervalStartJob - TimeSpan.FromMilliseconds(500));

            // Assert
            jobRepositoryMock.Verify(r => r.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task Start_ExecutionWithDelay_WithThrottle()
        {
            // Arrange
            var jobRepositoryMock = new Mock<IJobRepository>();

            var settings = new HorariumSettings
            {
                IntervalStartJob = TimeSpan.FromSeconds(1),
                JobThrottleSettings = new JobThrottleSettings
                {
                    UseJobThrottle = true,
                    IntervalMultiplier = 1,
                    JobRetrievalAttempts = 1
                }
            };

            var runnerJobs = new RunnerJobs(jobRepositoryMock.Object,
                settings,
                new JsonSerializerSettings(),
                Mock.Of<IHorariumLogger>(),
                Mock.Of<IExecutorJob>());

            // Act
            runnerJobs.Start();
            await Task.Delay(settings.IntervalStartJob - TimeSpan.FromMilliseconds(500));
            jobRepositoryMock.Invocations.Clear();

            await Task.Delay(settings.IntervalStartJob + settings.IntervalStartJob.Multiply(settings.JobThrottleSettings.IntervalMultiplier));
            
            // Assert
            jobRepositoryMock.Verify(r => r.GetReadyJob(It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Once);
        }
    }
}