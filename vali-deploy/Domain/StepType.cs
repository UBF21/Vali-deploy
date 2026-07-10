namespace vali_deploy.Domain;

public enum StepType
{
    GitCheckout,
    LocalCommand,
    DockerBuild,
    DockerRun,
    DockerPush,
    DockerSave,
    DockerLoad,
    DockerImagePrune,
    DockerComposePull,
    DockerComposeUp,
    DockerComposeDown,
    ZipPublishOutput,
    CopyToRemote,
    SshCommand,
    RawCommand
}
