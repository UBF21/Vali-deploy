using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IPipelineLogger
{
    void StartRun(string projectName, string subProjectName);
    void WriteStep(StepResult stepResult);
}
