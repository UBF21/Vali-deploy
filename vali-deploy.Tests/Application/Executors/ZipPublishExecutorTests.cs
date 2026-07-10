using System.IO.Compression;
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class ZipPublishExecutorTests
{
    private static StepExecutionContext Context(string projectPath, string subProjectName = "sub") => new()
    {
        ProjectName = "proj", SubProjectName = subProjectName, ProjectPath = projectPath,
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_ZipPublishOutput()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.ZipPublishOutput, executor.Handles);
    }

    [Fact]
    public async Task Runs_clean_build_and_publish_in_order_and_stops_on_first_failure()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null, null))
                .Callback<string, string, IDictionary<string, string>?, string?>((cmd, _, _, _) => callOrder.Add(cmd))
                .ReturnsAsync((string cmd, string _, IDictionary<string, string>? _, string? _) =>
                    cmd.Contains("build") ? new ProcessRunResult(1, "", "build failed") : new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.False(result.Success);
            Assert.DoesNotContain(callOrder, c => c.Contains("publish"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_fast_when_project_path_does_not_exist()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

        var result = await executor.ExecuteAsync(step, Context("/no/existe/este/path"));

        Assert.False(result.Success);
        Assert.Contains("no existe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Proceeds_past_clean_step_on_fresh_checkout_without_bin_or_obj()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null, null))
                .Callback<string, string, IDictionary<string, string>?, string?>((cmd, _, _, _) =>
                {
                    callOrder.Add(cmd);
                    // "dotnet publish" real generaría bin/Release/.../publish en disco; como IProcessRunner
                    // está mockeado (no ejecuta nada real), se simula ese efecto colateral para que la
                    // verificación de postcondición que agrega Task 10 (ZipPublishExecutor ahora busca la
                    // carpeta "publish" tras el build) encuentre algo. El resto del test -orden de comandos,
                    // ausencia de "&"- no cambia.
                    if (cmd.Contains("publish"))
                    {
                        Directory.CreateDirectory(Path.Combine(tempDir, "bin", "Release", "net7.0", "publish"));
                    }
                })
                .ReturnsAsync(new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.True(result.Success);
            Assert.Contains(callOrder, c => c.Contains("dotnet clean"));
            Assert.Contains(callOrder, c => c.Contains("dotnet build"));
            Assert.Contains(callOrder, c => c.Contains("dotnet publish"));

            if (OperatingSystem.IsWindows())
            {
                // rmdir encadenado con && (o incluso con & simple) puede fallar o enmascarar fallos.
                // El paso de limpieza debe mandar "bin" y "obj" como comandos independientes, sin
                // ningún operador de encadenamiento de shell.
                Assert.DoesNotContain(callOrder, c => c.Contains('&'));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_rmdir_chained_with_double_ampersand_fails_when_bin_and_obj_are_missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var runner = new ProcessRunner();

            var result = await runner.RunAsync("rmdir /s /q bin && rmdir /s /q obj", tempDir);

            Assert.NotEqual(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_split_clean_commands_succeed_independently_when_bin_and_obj_are_missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var runner = new ProcessRunner();

            var binResult = await runner.RunAsync("if exist bin rmdir /s /q bin", tempDir);
            var objResult = await runner.RunAsync("if exist obj rmdir /s /q obj", tempDir);

            Assert.Equal(0, binResult.ExitCode);
            Assert.Equal(0, objResult.ExitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_clean_step_sends_bin_and_obj_removal_as_independent_commands()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null, null))
                .Callback<string, string, IDictionary<string, string>?, string?>((cmd, _, _, _) => callOrder.Add(cmd))
                .ReturnsAsync(new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            await executor.ExecuteAsync(step, Context(tempDir));

            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("if exist bin rmdir /s /q bin", callOrder[0]);
                Assert.Equal("if exist obj rmdir /s /q obj", callOrder[1]);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Stops_before_dotnet_clean_when_bin_cleanup_fails_even_though_obj_cleanup_would_succeed()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null, null))
                .Callback<string, string, IDictionary<string, string>?, string?>((cmd, _, _, _) => callOrder.Add(cmd))
                .ReturnsAsync((string cmd, string _, IDictionary<string, string>? _, string? _) =>
                    cmd.Contains("bin")
                        ? new ProcessRunResult(5, "", "Acceso denegado: archivo en uso")
                        : new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.False(result.Success);
            Assert.Equal(5, result.ExitCode);
            Assert.DoesNotContain(callOrder, c => c.Contains("dotnet clean"));

            if (OperatingSystem.IsWindows())
            {
                // El fallo de "bin" no debe quedar enmascarado por el éxito posterior de "obj": al ser
                // comandos independientes evaluados uno por uno, "obj" nunca llega a ejecutarse.
                Assert.DoesNotContain(callOrder, c => c == "if exist obj rmdir /s /q obj");
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_rmdir_exit_code_does_not_reflect_locked_file_failures_even_when_unchained()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory().FullName;
        var binDir = Path.Combine(tempDir, "bin");
        Directory.CreateDirectory(binDir);
        var lockedFilePath = Path.Combine(binDir, "locked.dll");
        File.WriteAllText(lockedFilePath, "locked");

        using var lockedFileStream = new FileStream(lockedFilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        try
        {
            var runner = new ProcessRunner();

            var binResult = await runner.RunAsync("if exist bin rmdir /s /q bin", tempDir);

            // Hallazgo empírico en este entorno (Windows 11 / cmd.exe): "rmdir /s /q" NO pone un exit code
            // distinto de 0 cuando falla por un archivo bloqueado dentro del directorio -esto ocurre igual
            // como comando aislado, sin ningún "&"/"&&" de por medio-. Separar "bin" y "obj" en comandos
            // independientes (fix de esta ronda) elimina el enmascaramiento a nivel de shell, pero NO cierra
            // este caso puntual: el propio "rmdir" no reporta la falla via exit code. Cerrarlo requeriría que
            // ZipPublishExecutor verifique la post-condición (Directory.Exists) en vez de confiar solo en
            // ExitCode — eso queda fuera de esta ronda, documentado aquí para no perderlo.
            Assert.Equal(0, binResult.ExitCode);
            Assert.True(Directory.Exists(binDir),
                "bin sigue en disco pese a ExitCode=0 (gap conocido de rmdir, no cerrado por el fix de esta ronda).");
        }
        finally
        {
            lockedFileStream.Dispose();
            if (Directory.Exists(binDir))
            {
                Directory.Delete(binDir, recursive: true);
            }
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static string CreateFakePublishFolder(out string projectPath)
    {
        projectPath = Directory.CreateTempSubdirectory().FullName;
        var publishFolder = Path.Combine(projectPath, "bin", "Release", "net7.0", "publish");
        Directory.CreateDirectory(publishFolder);
        File.WriteAllText(Path.Combine(publishFolder, "app.dll"), "dummy");
        File.WriteAllText(Path.Combine(publishFolder, "app.pdb"), "dummy");
        return publishFolder;
    }

    private static Mock<IProcessRunner> SuccessfulBuildRunner(string projectPath)
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), projectPath, null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        return processRunner;
    }

    [Fact]
    public async Task Creates_zip_alongside_publish_folder_without_deleting_it()
    {
        var publishFolder = CreateFakePublishFolder(out var projectPath);
        var processRunner = SuccessfulBuildRunner(projectPath);

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };

        var result = await executor.ExecuteAsync(step, Context(projectPath, "sub"));

        Assert.True(result.Success);
        Assert.True(Directory.Exists(publishFolder));
        Assert.Equal(2, Directory.EnumerateFiles(publishFolder).Count());
        var zipFiles = Directory.EnumerateFiles(Path.GetDirectoryName(publishFolder)!, "sub-*.zip").ToList();
        Assert.Single(zipFiles);
    }

    [Fact]
    public async Task Excludes_OmitFiles_from_the_zip_but_not_from_the_publish_folder()
    {
        var publishFolder = CreateFakePublishFolder(out var projectPath);
        var processRunner = SuccessfulBuildRunner(projectPath);

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip", Args = { ["OmitFiles"] = "app.pdb" } };

        await executor.ExecuteAsync(step, Context(projectPath, "sub"));

        Assert.True(File.Exists(Path.Combine(publishFolder, "app.pdb")));

        var zipPath = Directory.EnumerateFiles(Path.GetDirectoryName(publishFolder)!, "sub-*.zip").Single();
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.Name == "app.dll");
        Assert.DoesNotContain(zip.Entries, e => e.Name == "app.pdb");
    }

    [Fact]
    public async Task Returns_failure_when_a_build_command_fails()
    {
        var _ = CreateFakePublishFolder(out var projectPath);
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), projectPath, null, null))
            .ReturnsAsync(new ProcessRunResult(1, "", "build error"));

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };

        var result = await executor.ExecuteAsync(step, Context(projectPath));

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
    }
}
