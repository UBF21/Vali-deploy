using vali_deploy;
using vali_deploy.Application;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests;

public class CompositionRootTests
{
    private static IStepExecutor[] BuildExecutors() =>
        CompositionRoot.BuildExecutors(new Mock<IProcessRunner>().Object, new Mock<ISshClientFactory>().Object);

    [Fact]
    public void BuildExecutors_registers_exactly_one_executor_for_every_StepType()
    {
        var executors = BuildExecutors();
        var handledTypes = executors.Select(e => e.Handles).ToList();

        foreach (var stepType in Enum.GetValues<StepType>())
        {
            Assert.True(
                handledTypes.Count(t => t == stepType) == 1,
                $"StepType.{stepType} debe tener exactamente un IStepExecutor registrado en CompositionRoot.BuildExecutors " +
                $"(encontrados: {handledTypes.Count(t => t == stepType)}).");
        }
    }

    [Fact]
    public void BuildExecutors_has_no_duplicate_Handles_values()
    {
        var executors = BuildExecutors();
        var handledTypes = executors.Select(e => e.Handles).ToList();

        var duplicates = handledTypes
            .GroupBy(t => t)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"StepType(s) con más de un executor registrado: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void CreatePipelineRunner_returns_a_usable_IPipelineRunner()
    {
        var runner = CompositionRoot.CreatePipelineRunner();

        Assert.IsType<PipelineRunner>(runner);
    }
}
