using vali_deploy.Domain;

namespace vali_deploy.Application;

public class StepExecutionContext
{
    public required string ProjectName { get; init; }
    public required string SubProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required DeployEnvironment Environment { get; init; }
}
