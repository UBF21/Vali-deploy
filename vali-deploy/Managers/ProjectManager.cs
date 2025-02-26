using System.Text.Json;
using Spectre.Console;
using vali_deploy.Models;

namespace vali_deploy.Managers
{
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
                return JsonSerializer.Deserialize<Dictionary<string, Project>>(json) ?? new Dictionary<string, Project>();
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
        /// Guarda la configuración de proyectos en el archivo JSON.
        /// </summary>
        public static void SaveConfig(Dictionary<string, Project> projects)
        {
            string json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            AnsiConsole.MarkupLine($"[green]Configuration saved to: {ConfigPath}[/]");
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
                    "Servicios-Back-Core",
                    new Project
                    {
                        Path = "/Users/isw21/Documents/ServiciosBackCore/Administracion",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "Maraia", Path = "" },
                            new() { Name = "grjjxs", Path = "" },
                            new() { Name = "Administration", Path = "IDSLatam.Service.Administracion.Api" }
                        }
                    }
                },
                {
                    "Servicios-Back",
                    new Project
                    {
                        Path = "\\Proyectos\\Servicios-Back",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "Manols", Path = "" },
                            new() { Name = "Pepe", Path = "" },
                            new() { Name = "Lurdes", Path = "" }
                        }
                    }
                },
                {
                    "Teseo-Services",
                    new Project
                    {
                        Path = "C:\\Proyectos\\Teseo-Services",
                        SubProjects = new List<SubProject>
                        {
                            new() { Name = "PapaNico", Path = "" },
                            new() { Name = "Mila", Path = "" },
                            new() { Name = "Munich", Path = "" }
                        }
                    }
                }
            };
        }
    }
}