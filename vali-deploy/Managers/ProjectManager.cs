using System.Text;
using System.Text.Json;
using Spectre.Console;
using vali_deploy.Models;

namespace vali_deploy.Managers
{
    /// <summary>
    /// Legacy CRUD de <see cref="Project"/>, persistido en el mismo archivo
    /// (<c>deploy_config.json</c>) que <see cref="vali_deploy.Infrastructure.ProjectRepository"/> usa
    /// para leer/escribir <see cref="vali_deploy.Domain.DeployConfig"/> (forma { Projects, Environments }).
    ///
    /// INTEROP: para evitar que ambas clases se pisen y destruyan datos (proyectos o
    /// DeployEnvironments) al escribir formas de JSON incompatibles sobre el mismo archivo,
    /// esta clase ahora:
    ///   1) Al leer, detecta si el JSON en disco ya tiene la forma { Projects, Environments }
    ///      (escrita por ProjectRepository) y extrae solo el nodo "Projects" en ese caso,
    ///      en vez de intentar deserializar el documento completo como Dictionary&lt;string,Project&gt;.
    ///   2) Al escribir, preserva el arreglo "Environments" existente (si lo hay) tal cual,
    ///      usando JsonElement/JsonDocument crudo (sin depender de vali_deploy.Domain.DeployEnvironment,
    ///      para no acoplar esta clase — candidata a retiro en la Tarea 30 — al modelo de dominio nuevo).
    ///
    /// Con esto, el archivo queda siempre en la forma { Projects, Environments } después del
    /// primer SaveConfig, y ProjectRepository.Load()/Save() puede seguir operando sobre el mismo
    /// archivo sin perder proyectos ni entornos. Ver Tarea 30 para el retiro completo de esta clase.
    /// </summary>
    public static class ProjectManager
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "vali-deploy");

        private static readonly string ConfigPath = Path.Combine(FolderPath, "deploy_config.json");

        /// <summary>
        /// Carga la configuración de proyectos o la crea si no existe.
        /// </summary>
        public static Dictionary<string, Project> LoadOrCreateConfig()
        {
            // Crear la carpeta si no existe
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
                AnsiConsole.MarkupLine($"[green]Created folder: {FolderPath}[/]");
            }

            // Crear el archivo JSON si no existe
            if (!File.Exists(ConfigPath))
            {
                var defaultProjects = GetDefaultProjects();
                SaveConfig(defaultProjects);
                AnsiConsole.MarkupLine($"[green]Configuration file created in: {ConfigPath}[/]");
                return defaultProjects;
            }

            // Cargar el archivo JSON existente
            try
            {
                string json = File.ReadAllText(ConfigPath);
                return ParseProjectsLeniently(json);
            }
            catch (JsonException)
            {
                AnsiConsole.MarkupLine("[red]:cross_mark: Error while deserializing the configuration file. The file will be recreated with the correct structure.[/]");
                var defaultProjects = GetDefaultProjects();
                SaveConfig(defaultProjects);
                return defaultProjects;
            }
        }

        /// <summary>
        /// Guarda la configuración de proyectos en el archivo JSON, preservando el nodo
        /// "Environments" existente (si lo hay) para no pisar datos escritos por
        /// <see cref="vali_deploy.Infrastructure.ProjectRepository"/>.
        /// </summary>
        public static void SaveConfig(Dictionary<string, Project> projects)
        {
            string? existingJson = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : null;
            string json = BuildCompatibleJson(projects, existingJson);
            File.WriteAllText(ConfigPath, json);
            AnsiConsole.MarkupLine($"[green]Configuration saved to: {ConfigPath}[/]");
        }

        /// <summary>
        /// Deserializa proyectos desde JSON tolerando ambas formas del archivo:
        /// - Forma legacy (flat): el documento raíz ES el Dictionary&lt;string, Project&gt;.
        /// - Forma DeployConfig (escrita por ProjectRepository): { "Projects": {...}, "Environments": [...] }.
        ///
        /// Se exige la presencia de AMBAS propiedades "Projects" y "Environments" en la raíz
        /// para clasificar el documento como forma DeployConfig — esto evita falsos positivos
        /// en el (extremadamente improbable) caso de que exista un proyecto legacy llamado
        /// literalmente "Projects" en la forma flat.
        /// </summary>
        internal static Dictionary<string, Project> ParseProjectsLeniently(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("Projects", out var projectsElement) &&
                root.TryGetProperty("Environments", out _))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Project>>(projectsElement.GetRawText())
                       ?? new Dictionary<string, Project>();
            }

            // Forma legacy flat: el documento raíz es directamente el diccionario de proyectos.
            return JsonSerializer.Deserialize<Dictionary<string, Project>>(json) ?? new Dictionary<string, Project>();
        }

        /// <summary>
        /// Construye el JSON de salida en la forma compatible con DeployConfig
        /// ({ "Projects": ..., "Environments": [...] }), preservando el arreglo "Environments"
        /// del JSON existente (si lo hay) sin necesitar conocer su esquema — se copia el
        /// JsonElement crudo tal cual, evitando acoplar esta clase a vali_deploy.Domain.DeployEnvironment.
        /// Si no hay JSON existente o no tiene "Environments", se escribe un arreglo vacío,
        /// lo cual "actualiza" el archivo a la forma nueva en el primer save y es compatible
        /// con ProjectRepository.Load() (que maneja Environments: [] sin problema).
        /// </summary>
        internal static string BuildCompatibleJson(Dictionary<string, Project> projects, string? existingJson)
        {
            JsonElement? environmentsElement = TryExtractEnvironmentsElement(existingJson);
            return WriteCompatibleJson(projects, environmentsElement);
        }

        /// <summary>
        /// Extrae (clonado) el JsonElement crudo de "Environments" del JSON existente, si lo hay.
        /// Devuelve null si no hay JSON previo, el documento está corrupto, o no tiene "Environments"
        /// — en cualquiera de esos casos, BuildCompatibleJson escribirá un arreglo vacío por defecto.
        /// </summary>
        private static JsonElement? TryExtractEnvironmentsElement(string? existingJson)
        {
            if (string.IsNullOrWhiteSpace(existingJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(existingJson);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("Environments", out var envElement))
                {
                    // Clonar: el JsonDocument se dispone al salir de este método.
                    return envElement.Clone();
                }

                return null;
            }
            catch (JsonException)
            {
                // JSON existente corrupto: no hay Environments que preservar, se sigue
                // con el arreglo vacío por defecto (mismo criterio de recuperación que
                // ProjectRepository.Load() usa ante un JsonException).
                return null;
            }
        }

        /// <summary>
        /// Escribe { "Projects": ..., "Environments": ... } usando Utf8JsonWriter, copiando el
        /// JsonElement de Environments tal cual si se proporcionó, o un arreglo vacío si no.
        /// </summary>
        private static string WriteCompatibleJson(Dictionary<string, Project> projects, JsonElement? environmentsElement)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();

                writer.WritePropertyName("Projects");
                JsonSerializer.Serialize(writer, projects);

                writer.WritePropertyName("Environments");
                if (environmentsElement.HasValue)
                {
                    environmentsElement.Value.WriteTo(writer);
                }
                else
                {
                    writer.WriteStartArray();
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>
        /// Agrega un nuevo proyecto a la configuración.
        /// </summary>
        public static void AddProject(string name, Project project)
        {
            var projects = LoadOrCreateConfig();
            if (!projects.TryAdd(name.Trim(), project))
            {
                AnsiConsole.MarkupLine($"[yellow]:warning: Project '{name}' already exists.[/]");
                return;
            }

            SaveConfig(projects);
            AnsiConsole.MarkupLine($"[green]Project '{name}' added successfully.[/]");
        }

        /// <summary>
        /// Elimina un proyecto de la configuración.
        /// </summary>
        public static void RemoveProject(string name)
        {
            var projects = LoadOrCreateConfig();
            if (!projects.ContainsKey(name.Trim()))
            {
                AnsiConsole.MarkupLine($"[yellow]:warning: Project '{name}' does not exist.[/]");
                return;
            }

            projects.Remove(name);
            SaveConfig(projects);
            AnsiConsole.MarkupLine($"[green]Project '{name}' removed successfully.[/]");
        }

        /// <summary>
        /// Obtiene la configuración por defecto.
        /// </summary>
        private static Dictionary<string, Project> GetDefaultProjects()
        {
            return new Dictionary<string, Project>
            {
                {
                    "Project 1",
                    new Project
                    {
                        Path = "\\Projects\\Path",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "SubProject 1", Path = "" },
                            new() { Name = "SubProject 2", Path = "" },
                            new() { Name = "SubProject 3", Path = "" }
                        }
                    }
                },
                {
                    "Project 2",
                    new Project
                    {
                        Path = "\\Projects\\Path",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "SubProject 1", Path = "" },
                            new() { Name = "SubProject 2", Path = "" },
                            new() { Name = "SubProject 3", Path = "" }
                        }
                    }
                },
                {
                    "Project 3",
                    new Project
                    {
                        Path = "C:\\Projects\\Path",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "SubProject 1", Path = "" },
                            new() { Name = "SubProject 1", Path = "" },
                            new() { Name = "SubProject 1", Path = "" }
                        }
                    }
                }
            };
        }
    }
}