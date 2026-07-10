using vali_deploy.Application.Executors;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application.Executors;

public class DockerRunExecutorTests
{
    // DockerRunExecutor arranca una sesión interactiva real (docker run -it) heredando la consola
    // del proceso padre — no usa IProcessRunner, así que no es mockeable/testeable en aislamiento sin
    // una capa de abstracción de Process que este proyecto no tiene (igual que Presentation/ no tiene
    // tests unitarios). Este test solo verifica el registro correcto del StepType.
    [Fact]
    public void Handles_DockerRun()
    {
        var executor = new DockerRunExecutor();
        Assert.Equal(StepType.DockerRun, executor.Handles);
    }
}
