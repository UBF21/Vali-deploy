namespace vali_deploy.Domain;

public enum StepType
{
    GitCheckout,
    LocalCommand,
    DockerBuild,
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
