# Horarium

[![Build Status](https://cloud.drone.io/api/badges/TinkoffCreditSystems/Horarium/status.svg)](https://cloud.drone.io/TinkoffCreditSystems/Horarium)
[![Nuget](https://img.shields.io/nuget/v/Horarium.svg)](https://www.nuget.org/packages/Horarium)
[![codecov](https://codecov.io/gh/TinkoffCreditSystems/Horarium/branch/master/graph/badge.svg)](https://codecov.io/gh/TinkoffCreditSystems/Horarium)
[![codefactor](https://www.codefactor.io/repository/github/tinkoffcreditsystems/horarium/badge)](https://www.codefactor.io/repository/github/tinkoffcreditsystems/horarium)

Horarium is an open source job scheduling .NET library with an easy to use API, that can be integrated within applications of any scale - from the smallest stand-alone application to the largest e-commerce system.

Horarium is fully based on an asynchronous work model, it allows you to run hundreds of parallel jobs within a single application instance. It supports jobs execution in distributed systems and uses MongoDB as a synchronization backend.

Horarium supports .NET Core/netstandard 2.0 and .NET Framework 4.6.2 and later.

Support Databases

| Database   | Support                                                                 |
| ---------- | ----------------------------------------------------------------------- |
| MongoDB    | Yes                                                                     |
| In Memory  | Yes |
| PostgreSQL | Not yet [#6](https://github.com/TinkoffCreditSystems/Horarium/issues/6) |

## Getting started

Add nuget-package Horarium

```bash
dotnet add package Horarium
dotnet add package Horarium.Mongo
```

Add job that implements interface ```IJob<T>```

```csharp
public class TestJob : IJob<int>
{
    public async Task Execute(int param)
    {
        Console.WriteLine(param);
        await Task.Run(() => { });
    }
}
```

Create ```HorariumServer``` and schedule ```TestJob```

```csharp
var horarium = new HorariumServer(new InMemoryRepository());
horarium.Start();
await horarium.Create<TestJob, int>(666)
        .Schedule();
```

## Add to ```Asp.Net core``` application

Add nuget-package Horarium.AspNetCore

```bash
dotnet add package Horarium.AspNetCore
```

Add  ```Horarium```  in Asp.NET Core DI

```csharp
public void ConfigureServices(IServiceCollection services)
{
    //...
    services.AddHorariumServer(MongoRepositoryFactory.Create("mongodb://localhost:27017/horarium"));
    //...
}
```

Start HorariumServer in Asp.NET Core application

```csharp
public void Configure(IApplicationBuilder app)
{
    //...
    app.ApplicationServices.StartHorariumServer();
    //...
}
```

Inject interface ```IHorarium``` into Controller

```csharp

private readonly IHorarium _horarium;

public HomeController(IHorarium horarium)
{
    _horarium = horarium;
}

[Route("api")]
public class HomeController : Controller
{
    [HttpPost]
    public async Task Run(int count)
    {
            await _horarium.Create<TestJob, int>(count)
                          .Schedule();
    }
}
```

## Create Recurrent Job

Add job that implements interface ```IJobRecurrent```

```csharp
public class TestRecurrentJob : IJobRecurrent
    {
        public Task Execute()
        {
            Console.WriteLine("Run -" + DateTime.Now);
            return Task.CompletedTask;
        }
    }
```

Schedule ```TestRecurrentJob``` to run every 15 seconds

```csharp
await horarium.CreateRecurrent<TestRecurrentJob>(Cron.SecondInterval(15))
                .Schedule();
```

## Create sequence of jobs

Sometimes you need to create sequence of jobs, where every next job would run if and only if previous job succeeds. If any job of the sequence fails next jobs won't run

```csharp
await horarium
    .Create<TestJob, int>(1) // 1-st job
    .Next<TestJob, int>(2) // 2-nd job
    .Next<TestJob, int>(3) // 3-rd job
    .Schedule();
```

## Distributed Horarium

![Distributed Scheme](DistributedScheme.png)

Horarium has two types of workers: server and client. Server can run jobs and schedule new jobs, while client can only schedule new jobs.

Horarium guarantees that a job would run **exactly once**

## Things to watch out for

Every Horarium instance consults MongoDB about new jobs to run every 100ms (default), thus creating some load on the DB server. This interval can be changed in ```HorariumSettings```

If you want to decrease load, you can use job throttling that will automatically increase interval if there are no jobs available after certain attempts. To enable this feature, pass `JobThrottleSettings` to `HorariumSettings` with property `UseJobThrottle` set to `true`.

```csharp
var settings = new HorariumSettings
{
    JobThrottleSettings = new JobThrottleSettings
    {
        UseJobThrottle = true
    }
};
```

For more information about configuration, see `JobThrottleSettings`

## Using Horarium with SimpleInjector

To use Horarium with SimpleInjector one should implement its own `IJobFactory`, using `Container` from `SimpleInjector`. For example:
```csharp
public class SimpleInjectorJobScopeFactory : IJobScopeFactory
{
    private readonly Container _container;

    public SimpleInjectorJobScopeFactory(Container container)
    {
        _container = container;
    }

    public IJobScope Create()
    {
        var scope = AsyncScopedLifestyle.BeginScope(_container);
        return new SimpleInjectorJobScope(scope);
    }
}

public class SimpleInjectorJobScope : IJobScope
{
    private readonly Scope _scope;

    public SimpleInjectorJobScope(Scope scope)
    {
        _scope = scope;
    }

    public object CreateJob(Type type)
    {
        return _scope.GetInstance(type);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}
```

Then add `HorariumServer` (or `HorariumClient`):

```csharp
container.RegisterSingleton<IHorarium>(() =>
{
    var settings = new HorariumSettings
    {
        JobScopeFactory = new SimpleInjectorJobScopeFactory(container),
        Logger = new YourHorariumLogger()
    };

    return new HorariumServer(jobRepository, settings);
});
```

In case of `HorariumServer`, don't forget to start it in your entypoint:

```csharp
((HorariumServer) container.GetInstance<IHorarium>()).Start();
```

## Failed repeat strategy for jobs

When a job fails, Horarium can handle this exception with the same strategy.
By default, the job repeats 10 times with delays of 10 minutes, 20 minutes, 30 minutes and etc.
You can override this strategy using `IFailedRepeatStrategy` interface.

Example of default `DefaultRepeatStrategy` implementation:

```csharp
public class DefaultRepeatStrategy :IFailedRepeatStrategy
{
    public TimeSpan GetNextStartInterval(int countStarted)
    {
        const int increaseRepeat = 10;
        return TimeSpan.FromMinutes(increaseRepeat * countStarted);
    }
}
```

This class is called every time when a job fails, and it has to return `TimeSpan` of the next scheduled job run.
To override default behavior globally, change settings in ```HorariumSettings```

```csharp
new HorariumSettings
{
    FailedRepeatStrategy = new CustomFailedRepeatStrategy(),
    MaxRepeatCount = 7
});
```

To override the default behavior for a particular job:

```csharp
await horarium.Create<TestJob, int>(666)
    .MaxRepeatCount(5)
    .AddRepeatStrategy<DefaultRepeatStrategy>()
    .Schedule();
```

If you want to disable all repeats, just set `MaxRepeatCount` to 1

```csharp
new HorariumSettings
{
    MaxRepeatCount = 1
});
```

