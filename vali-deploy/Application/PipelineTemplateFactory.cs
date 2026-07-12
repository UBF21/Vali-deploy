using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineTemplateFactory
{
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, string remoteDeployPath, string composeFileName, DockerRegistry? dockerRegistry = null)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
        var remoteComposeFilePath = $"{remoteDeployPath}/{composeFileName}";
        var registryTag = BuildRegistryTag(dockerRegistry, imageTag);

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.DockerBuild, Name = "Build imagen", Args = { ["ImageTag"] = imageTag, ["Dockerfile"] = "Dockerfile" } },
            new()
            {
                Type = StepType.DockerPush, Name = "Push a registry",
                Args =
                {
                    ["ImageTag"] = imageTag,
                    ["RegistryTag"] = registryTag,
                    ["RegistryHost"] = dockerRegistry?.Host ?? "",
                    ["RegistryUsername"] = dockerRegistry?.Username ?? "",
                    ["RegistryTokenEnvVar"] = dockerRegistry?.TokenEnvVar ?? ""
                }
            },
            new() { Type = StepType.CopyToRemote, Name = $"Copiar {composeFileName}", Args = { ["LocalPath"] = composeFileName, ["RemotePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposePull, Name = "Compose pull", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerImagePrune, Name = "Limpiar imágenes viejas", Args = { ["ImageNameFilter"] = $"{projectName.ToLower()}-{subProjectName.ToLower()}" } }
        };
    }

    public List<DeployStep> CreateDockerComposeRemoteBuildTemplate(string remoteDeployPath, string composeFileName)
    {
        var remoteComposeFilePath = $"{remoteDeployPath}/{composeFileName}";

        return new List<DeployStep>
        {
            new() { Type = StepType.SshCommand, Name = "Actualizar código", Args = { ["Command"] = $"cd {remoteDeployPath} && git pull" } },
            new() { Type = StepType.DockerComposeBuild, Name = "Compose build", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } }
        };
    }

    private static string BuildRegistryTag(DockerRegistry? registry, string imageTag)
    {
        if (registry == null || string.IsNullOrEmpty(registry.Username)) return imageTag;
        var prefix = string.IsNullOrEmpty(registry.Host) ? registry.Username : $"{registry.Host}/{registry.Username}";
        return $"{prefix}/{imageTag}";
    }

    public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName, string remoteDeployPath, List<string>? omitFiles = null)
    {
        var omitFilesArg = omitFiles is { Count: > 0 } ? string.Join("|", omitFiles) : "";
        var remoteZipPath = $"{remoteDeployPath}/{subProjectName.ToLower()}.zip";

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.ZipPublishOutput, Name = "Build, publish y comprimir output", Args = { ["OmitFiles"] = omitFilesArg } },
            new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto", Args = { ["RemotePath"] = remoteZipPath } },
            new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
            new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
        };
    }

    public static string ResolveDefaultRemoteDeployPath(string projectName, string subProjectName, DeployEnvironment environment) =>
        environment.RemoteDeployPath ?? $"/opt/{projectName.ToLower()}-{subProjectName.ToLower()}";

    public List<DeployStep> CreateLocalPublishTemplate(List<string> omitFiles) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.ZipPublishOutput,
                Name = "Build, publish y comprimir output",
                Args = { ["OmitFiles"] = omitFiles.Count > 0 ? string.Join("|", omitFiles) : "" }
            }
        };

    public List<DeployStep> CreateLocalDockerBuildTemplate(string dockerfilePath, string imageTag, string? buildArgs) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerBuild,
                Name = "Build imagen",
                Args = { ["Dockerfile"] = dockerfilePath, ["ImageTag"] = imageTag, ["BuildArgs"] = buildArgs ?? "" }
            }
        };

    public List<DeployStep> CreateLocalDockerPushTemplate(string imageTag, DockerRegistry? dockerRegistry) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerPush,
                Name = "Push a registry",
                Args =
                {
                    ["ImageTag"] = imageTag,
                    ["RegistryTag"] = BuildRegistryTag(dockerRegistry, imageTag),
                    ["RegistryHost"] = dockerRegistry?.Host ?? "",
                    ["RegistryUsername"] = dockerRegistry?.Username ?? "",
                    ["RegistryTokenEnvVar"] = dockerRegistry?.TokenEnvVar ?? ""
                }
            }
        };

    public List<DeployStep> CreateLocalDockerRunTemplate(string imageTag, string? runArgs) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerRun,
                Name = "Run contenedor",
                Args = { ["ImageTag"] = imageTag, ["RunArgs"] = runArgs ?? "" }
            }
        };
}
