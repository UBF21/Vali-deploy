using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineTemplateFactory
{
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, DeployEnvironment environment)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
        var remoteDeployPath = environment.RemoteDeployPath ?? $"/opt/{projectName.ToLower()}-{subProjectName.ToLower()}";
        var remoteComposeFilePath = $"{remoteDeployPath}/compose.yml";

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.DockerBuild, Name = "Build imagen", Args = { ["ImageTag"] = imageTag, ["Dockerfile"] = "Dockerfile" } },
            new() { Type = StepType.DockerPush, Name = "Push a registry", Args = { ["ImageTag"] = imageTag, ["RegistryTag"] = "" } },
            new() { Type = StepType.CopyToRemote, Name = "Copiar compose.yml", Args = { ["LocalPath"] = "compose.yml", ["RemotePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposePull, Name = "Compose pull", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerImagePrune, Name = "Limpiar imágenes viejas", Args = { ["ImageNameFilter"] = $"{projectName.ToLower()}-{subProjectName.ToLower()}" } }
        };
    }

    public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName)
    {
        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.LocalCommand, Name = "Limpiar bin/obj", Args = { ["Command"] = OperatingSystem.IsWindows() ? "rmdir /s /q bin && rmdir /s /q obj" : "rm -rf bin obj" } },
            new() { Type = StepType.LocalCommand, Name = "dotnet publish", Args = { ["Command"] = "dotnet publish -c Release" } },
            new() { Type = StepType.ZipPublishOutput, Name = "Comprimir output" },
            new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto" },
            new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
            new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
        };
    }
}
