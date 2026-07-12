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
        var interactiveLauncher = new InteractiveProcessLauncher();

        return new PipelineRunner(BuildExecutors(processRunner, sshClientFactory, secretResolver, interactiveLauncher));
    }

    /// <summary>
    /// Builds the full set of <see cref="IStepExecutor"/> instances that back <see cref="CreatePipelineRunner"/>.
    /// Extracted as its own seam (rather than inlined in <see cref="CreatePipelineRunner"/>) so tests can assert
    /// registration completeness against every <see cref="vali_deploy.Domain.StepType"/> value without needing
    /// real infrastructure (a live process runner or SSH connection) or introspecting a private dictionary.
    /// </summary>
    public static IStepExecutor[] BuildExecutors(IProcessRunner processRunner, ISshClientFactory sshClientFactory, ISecretResolver secretResolver, IInteractiveProcessLauncher interactiveLauncher) =>
        new IStepExecutor[]
        {
            new LocalCommandExecutor(processRunner),
            new RawCommandExecutor(processRunner),
            new GitCheckoutExecutor(processRunner),
            new DockerBuildExecutor(processRunner),
            new DockerRunExecutor(interactiveLauncher),
            new DockerPushExecutor(processRunner, secretResolver),
            new DockerSaveExecutor(processRunner),
            new DockerImagePruneExecutor(processRunner),
            new ZipPublishExecutor(processRunner),
            new SshCommandExecutor(sshClientFactory),
            new DockerLoadExecutor(sshClientFactory),
            new CopyToRemoteExecutor(sshClientFactory),
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeBuildExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
        };

    public static IProjectRepository CreateProjectRepository() => new ProjectRepository();

    public static IPipelineLogger CreatePipelineLogger() => new PipelineLogger();

    public static IDeployHistoryRepository CreateDeployHistoryRepository() => new DeployHistoryRepository();
}
