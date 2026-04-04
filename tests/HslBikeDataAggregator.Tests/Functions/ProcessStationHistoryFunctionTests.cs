using System.Reflection;

using HslBikeDataAggregator.Functions;

using Microsoft.Azure.Functions.Worker;

namespace HslBikeDataAggregator.Tests.Functions;

public sealed class ProcessStationHistoryFunctionTests
{
    [Fact]
    public void ProcessStationHistory_UsesConfiguredCronExpression()
    {
        var method = typeof(ProcessStationHistoryFunction).GetMethod(nameof(ProcessStationHistoryFunction.ProcessStationHistory), BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);

        var timerParameter = method!.GetParameters()[0];
        var timerTriggerAttribute = timerParameter.GetCustomAttribute<TimerTriggerAttribute>();

        Assert.NotNull(timerTriggerAttribute);
        Assert.Equal("%HistoryProcessingCron%", timerTriggerAttribute!.Schedule);
    }
}
