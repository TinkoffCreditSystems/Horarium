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
    public class ExecutorJobTest
    {
        [Fact]
        public async Task GetJobFromScope_ThenDisposeScope()
        {
            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestJob());

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = "StringParams",
                JobType = typeof(TestJob)
            });

            jobScopeFactoryMock.Verify(x => x.Create(), Times.Once);
            jobScopeMock.Verify(x => x.CreateJob(typeof(TestJob)), Times.Once);
            jobScopeMock.Verify(x => x.Dispose(), Times.Once);
        }

        [Fact]
        public async Task ExceptionInJob_SaveError()
        {
            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestFailedJob());

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = new object(),
                JobType = typeof(TestFailedJob)
            });

            jobRepositoryMock.Verify(x =>
                x.RepeatJob(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<NotImplementedException>()));
        }

        [Fact]
        public async Task ExceptionCreateJob_LoggingError()
        {
            var jobLoggingMock = new Mock<IHorariumLogger>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Throws<Exception>();

            var executorJob = new ExecutorJob(
                Mock.Of<IJobRepository>(),
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    Logger = jobLoggingMock.Object
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = new object(),
                JobType = typeof(TestFailedJob)
            });

            jobLoggingMock.Verify(x =>
                x.Error($"Ошибка создания джоба {typeof(TestFailedJob)}", It.IsAny<Exception>()));
        }

        [Fact]
        public async Task ExceptionInJob_SaveError_WhenAllRepeatesIsFailed()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            var job = new TestAllRepeatesIsFailedJob();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => job);

            var executorJob = new ExecutorJob(
                Mock.Of<IJobRepository>(),
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = new object(),
                JobType = typeof(TestAllRepeatesIsFailedJob),
                CountStarted = 10
            });

            Assert.True(job.FailedEventCalled);
        }

        [Fact]
        public async Task ExceptionInJob_SaveError_WhenNotAllRepeatesIsFailed()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            var job = new TestAllRepeatesIsFailedJob();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => job);

            var executorJob = new ExecutorJob(
                Mock.Of<IJobRepository>(),
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = new object(),
                JobType = typeof(TestAllRepeatesIsFailedJob),
                CountStarted = 1
            });

            Assert.False(job.FailedEventCalled);
        }

        [Fact]
        public async Task RecurrentJob_RescheduleAfterRun()
        {
            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            const string cron = "*/15 * * * * *";

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestReccurrentJob());
            jobRepositoryMock.Setup(x => x.GetCronForRecurrentJob(It.IsAny<string>()))
                .ReturnsAsync(cron);

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            await executorJob.Execute(new JobMetadata()
            {
                JobParam = null,
                JobType = typeof(TestReccurrentJob),
                CountStarted = 1,
                Cron = cron
            });

            jobRepositoryMock.Verify(x => x.RescheduleRecurrentJob(It.IsAny<string>(), It.IsAny<DateTime>(), null));
        }

        [Fact]
        public async Task RecurrentJob_ThrowException_AddRecurrentJobNextStart()
        {
            // Arrange
            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();
            var jobAdderJob = new Mock<IAdderJobs>();

            const string cron = "*/15 * * * * *";

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => throw new Exception());
            jobRepositoryMock.Setup(x => x.GetCronForRecurrentJob(It.IsAny<string>()))
                .ReturnsAsync(cron);

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                jobAdderJob.Object,
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            var job = new JobMetadata()
            {
                JobParam = null,
                JobKey = nameof(TestReccurrentJob),
                JobType = typeof(TestReccurrentJob),
                CountStarted = 1,
                Cron = cron
            };

            // Act
            await executorJob.Execute(job);

            // Assert
            jobRepositoryMock.Verify(x => x.GetCronForRecurrentJob(It.IsAny<string>()), Times.Once);
            jobAdderJob.Verify(x => x.AddRecurrentJob(It.Is<JobMetadata>(j => j.JobType == job.JobType &&
                                                                              j.JobKey == job.JobKey &&
                                                                              j.Cron == job.Cron)));
        }

        [Fact]
        public async Task RecurrentJob_ExceptionCreateJob_LoggingError()
        {
            // Arrange
            var jobLoggingMock = new Mock<IHorariumLogger>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();
            var jobRepositoryMock = new Mock<IJobRepository>();

            const string cron = "*/15 * * * * *";

            jobRepositoryMock.Setup(x => x.GetCronForRecurrentJob(It.IsAny<string>()))
                .ReturnsAsync(cron);
            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Throws<Exception>();

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    Logger = jobLoggingMock.Object
                });

            // Act
            await executorJob.Execute(new JobMetadata
            {
                JobParam = null,
                JobKey = nameof(TestReccurrentJob),
                JobType = typeof(TestReccurrentJob),
                CountStarted = 1,
                Cron = cron
            });

            // Assert
            jobLoggingMock.Verify(x =>
                x.Error($"Ошибка создания джоба {typeof(TestReccurrentJob)}", It.IsAny<Exception>()));
        }

        [Fact]
        public async Task JobWithNextJob_DelayIsNull_AddedJobStartAtEqualNow()
        {
            var startAt = new DateTime(2018, 10, 11, 15, 25, 0);

            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestJob());

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                });

            var jobExecuteTime = DateTime.UtcNow;

            await executorJob.Execute(new JobMetadata()
            {
                JobParam = "StringParams",
                JobType = typeof(TestJob),
                CountStarted = 1,
                Cron = "*/15 * * * * *",
                NextJob = new JobMetadata
                {
                    JobParam = "StringParams",
                    JobType = typeof(TestJob),
                    StartAt = startAt,
                    Delay = null
                }
            });

            jobRepositoryMock.Verify(x => x.AddJob(It.Is<JobDb>(job => job.StartAt >= jobExecuteTime
                                                                       && job.StartAt <= DateTime.Now)));
        }

        [Fact]
        public async Task JobWithNextJob_DelayIsZero_AddedJobStartAtEqualNow()
        {
            var startAt = new DateTime(2018, 10, 11, 15, 25, 0);

            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestJob());

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            var jobExecuteTime = DateTime.UtcNow;

            await executorJob.Execute(new JobMetadata
            {
                JobParam = "StringParams",
                JobType = typeof(TestJob),
                CountStarted = 1,
                Cron = "*/15 * * * * *",
                NextJob = new JobMetadata
                {
                    JobParam = "StringParams",
                    JobType = typeof(TestJob),
                    StartAt = startAt,
                    Delay = TimeSpan.Zero
                }
            });

            jobRepositoryMock.Verify(x => x.AddJob(It.Is<JobDb>(job => job.StartAt >= jobExecuteTime
                                                                       && job.StartAt <= DateTime.Now)));
        }

        [Fact]
        public async Task JobWithNextJob_DelayContains_AddedJobStartAtEqualNowPlusDelay()
        {
            var startAt = new DateTime(2018, 10, 11, 15, 25, 0);
            var delay = TimeSpan.FromHours(4);

            var jobRepositoryMock = new Mock<IJobRepository>();
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestJob());

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object
                });

            var jobExecuteTime = DateTime.UtcNow;

            await executorJob.Execute(new JobMetadata()
            {
                JobParam = "StringParams",
                JobType = typeof(TestJob),
                CountStarted = 1,
                Cron = "*/15 * * * * *",
                NextJob = new JobMetadata
                {
                    JobParam = "StringParams",
                    JobType = typeof(TestJob),
                    StartAt = startAt,
                    Delay = delay
                }
            });

            jobRepositoryMock.Verify(x => x.AddJob(It.Is<JobDb>(job => job.StartAt != startAt
                                                                       && job.StartAt >= jobExecuteTime + delay)));
        }
        
        [Fact]
        public async Task ThrowExceptionJobWithoutFailedStrategy_UseDefaultFailedStrategy()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestFailedJob());
            
            var failedRepeatStrategyMock = new Mock<IFailedRepeatStrategy>();

            var executorJob = new ExecutorJob(
                Mock.Of<IJobRepository>(),
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    FailedRepeatStrategy = failedRepeatStrategyMock.Object
                });

            await executorJob.Execute(new JobMetadata(){
                JobParam = new object(),
                JobType = typeof(TestFailedJob)
            });

            failedRepeatStrategyMock.Verify(x=>x.GetNextStartInterval(It.IsAny<int>()));
        }
        
        [Fact]
        public async Task ThrowExceptionJobWithFailedStrategy_UseFailedStrategyFromJob()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestFailedJob());
            
            var failedRepeatStrategyMock = new Mock<IFailedRepeatStrategy>();

            var executorJob = new ExecutorJob(
                Mock.Of<IJobRepository>(),
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    FailedRepeatStrategy = failedRepeatStrategyMock.Object
                });

            await executorJob.Execute(new JobMetadata(){
                JobParam = new object(),
                JobType = typeof(TestFailedJob),
                RepeatStrategy = typeof(DefaultRepeatStrategy)
            });

            failedRepeatStrategyMock.Verify(x=>x.GetNextStartInterval(It.IsAny<int>()), Times.Never);
        }
        
        [Fact]
        public async Task ThrowExceptionJobCountStartedEqMax_DontCallFailedStrategyAndFailedJob()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();
            
            var jobRepositoryMock = new Mock<IJobRepository>();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestFailedJob());
            
            var failedRepeatStrategyMock = new Mock<IFailedRepeatStrategy>();

            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    FailedRepeatStrategy = failedRepeatStrategyMock.Object,
                    MaxRepeatCount = 10
                });

            await executorJob.Execute(new JobMetadata(){
                JobParam = new object(),
                JobType = typeof(TestFailedJob),
                CountStarted = 10
            });

            failedRepeatStrategyMock.Verify(x=>x.GetNextStartInterval(It.IsAny<int>()), Times.Never);
            jobRepositoryMock.Verify(x=>x.FailedJob(It.IsAny<string>(), It.IsAny<Exception>()));
        }

        [Fact]
        public async Task FailedEventOfAJobThrows_FailedJobCalledNevertheless()
        {
            var (jobScopeFactoryMock, jobScopeMock) = CreateScopeMock();
            var jobRepositoryMock = new Mock<IJobRepository>();

            jobScopeMock.Setup(x => x.CreateJob(It.IsAny<Type>()))
                .Returns(() => new TestFailedEventThrowsJob());
            
            var executorJob = new ExecutorJob(
                jobRepositoryMock.Object,
                Mock.Of<IAdderJobs>(),
                new HorariumSettings
                {
                    JobScopeFactory = jobScopeFactoryMock.Object,
                    MaxRepeatCount = 10
                });

            await executorJob.Execute(new JobMetadata
            {
                JobParam = new object(),
                JobType = typeof(TestFailedEventThrowsJob),
                CountStarted = 10
            });
            
            jobRepositoryMock.Verify(x => x.FailedJob(It.IsAny<string>(), It.IsAny<Exception>()));
        }

        private static (Mock<IJobScopeFactory> jobScopeFactoryMock, Mock<IJobScope> jobScopeMock) CreateScopeMock()
        {
            var jobScopeMock = new Mock<IJobScope>();
            var jobScopeFactoryMock = new Mock<IJobScopeFactory>();
            jobScopeFactoryMock.Setup(x => x.Create()).Returns(jobScopeMock.Object);

            return (jobScopeFactoryMock, jobScopeMock);
        }

        public class TestFailedJob : IJob<object>
        {
            public Task Execute(object param)
            {
                throw new NotImplementedException();
            }
        }

        public sealed class TestAllRepeatesIsFailedJob : IAllRepeatesIsFailed, IJob<object>
        {
            public Task Execute(object param)
            {
                throw new NotImplementedException();
            }

            public bool FailedEventCalled { get; set; }

            public Task FailedEvent(object param, Exception ex)
            {
                FailedEventCalled = true;
                return Task.CompletedTask;
            }
        }

        public class TestFailedEventThrowsJob : IAllRepeatesIsFailed, IJob<object>
        {
            public Task Execute(object param)
            {
                throw new NotImplementedException();
            }
            
            public Task FailedEvent(object param, Exception ex)
            {
                throw new NotImplementedException();
            }
        }
    }
}