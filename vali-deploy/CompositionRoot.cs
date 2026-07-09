using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Infrastructure;

namespace vali_deploy;

public static class CompositionRoot
{
    public static IPipelineRunner CreatePipelineRunner()
    {
        var processRunner = new ProcessRunner();
        var secretResolver = new EnvVarSecretResolver();
        var sshClientFactory = new SshClientFactory(secretResolver);

        IStepExecutor[] executors =
        {
            new LocalCommandExecutor(processRunner),
            new RawCommandExecutor(processRunner),
            new GitCheckoutExecutor(processRunner),
            new DockerBuildExecutor(processRunner),
            new DockerPushExecutor(processRunner),
            new DockerSaveExecutor(processRunner),
            new DockerImagePruneExecutor(processRunner),
            new ZipPublishExecutor(processRunner),
            new SshCommandExecutor(sshClientFactory),
            new DockerLoadExecutor(sshClientFactory),
            new CopyToRemoteExecutor(sshClientFactory),
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
        };

        return new PipelineRunner(executors);
    }

    public static IProjectRepository CreateProjectRepository() => new ProjectRepository();

    public static IPipelineLogger CreatePipelineLogger() => new PipelineLogger();
}
