namespace vali_deploy.Presentation;

public static class Translator
{
    private static string _currentLanguage = "en";

    private static readonly Dictionary<string, string> EnToEs = new()
    {
        // Menú principal
        ["What do you want to do?"] = "¿Qué querés hacer?",
        ["Add Project"] = "Agregar Proyecto",
        ["Remove Project"] = "Eliminar Proyecto",
        ["Show Projects"] = "Ver Proyectos",
        ["Configure Publish File Omissions"] = "Configurar Archivos Omitidos de Publish",
        ["Remove Subprojects"] = "Eliminar Subproyectos",
        ["Manage Docker Projects"] = "Gestionar Proyectos Docker",
        ["Manage Publish Arguments"] = "Gestionar Argumentos de Publish",
        ["Manage Environments"] = "Gestionar Entornos",
        ["View Deploy History"] = "Ver Historial de Deploys",
        ["View Environments Tree"] = "Ver Árbol de Entornos",
        ["[seagreen1]Exit[/]"] = "[seagreen1]Salir[/]",

        // Navegación reutilizada
        ["[seagreen1]Back to Main Menu[/]"] = "[seagreen1]Volver al Menú Principal[/]",
        ["[seagreen1]Back to Projects Menu[/]"] = "[seagreen1]Volver al Menú de Proyectos[/]",
        ["[seagreen1]Back to Projects[/]"] = "[seagreen1]Volver a Proyectos[/]",
        ["[seagreen1]Back to Subprojects[/]"] = "[seagreen1]Volver a Subproyectos[/]",
        ["[seagreen1]Back[/]"] = "[seagreen1]Volver[/]",
        ["[seagreen1]Cancel[/]"] = "[seagreen1]Cancelar[/]",

        // Remover subproyectos
        ["Select projects to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los proyectos a eliminar (barra espaciadora para seleccionar, Enter para confirmar)",
        ["Select a project to remove subprojects from"] = "Elegí un proyecto para eliminarle subproyectos",
        ["Select subprojects to remove from project '{0}' (use spacebar to select, Enter to confirm)"] =
            "Elegí los subproyectos a eliminar del proyecto '{0}' (barra espaciadora para seleccionar, Enter para confirmar)",

        // Show Projects
        ["Select a project"] = "Elegí un proyecto",
        ["Select a subproject for project '{0}'"] = "Elegí un subproyecto del proyecto '{0}'",

        // Omitir archivos de publish
        ["Select a project to configure publish file omissions"] =
            "Elegí un proyecto para configurar archivos omitidos de publish",
        ["Select a subproject for project '{0}' to manage files to omit"] =
            "Elegí un subproyecto del proyecto '{0}' para gestionar archivos a omitir",
        ["Add file to omit"] = "Agregar archivo a omitir",
        ["Remove file from omit list"] = "Quitar archivo de la lista de omitidos",
        ["Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)"] =
            "Elegí los archivos a quitar de la lista de omitidos (barra espaciadora para seleccionar, Enter para confirmar)",

        // Ejecutar comando de subproyecto
        ["What do you want to do with subproject '{0}'?"] = "¿Qué querés hacer con el subproyecto '{0}'?",
        ["Generate Microsoft publish"] = "Generar publish de Microsoft",
        ["Edit Pipeline"] = "Editar Pipeline",
        ["Push to registry"] = "Subir al registry",

        // Proyectos/subproyectos Docker
        ["Select a project with Docker subprojects"] = "Elegí un proyecto con subproyectos Docker",
        ["Select a Docker subproject in '{0}'"] = "Elegí un subproyecto Docker en '{0}'",
        ["Add Docker Arg"] = "Agregar Argumento Docker",
        ["Remove Docker Args"] = "Quitar Argumentos Docker",
        ["Select argument type:"] = "Elegí el tipo de argumento:",
        ["Build Arg"] = "Argumento de Build",
        ["Run Arg"] = "Argumento de Run",
        ["Select argument type to remove:"] = "Elegí el tipo de argumento a quitar:",
        ["Build Args"] = "Argumentos de Build",
        ["Run Args"] = "Argumentos de Run",
        ["Select build args to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los argumentos de build a quitar (barra espaciadora para seleccionar, Enter para confirmar)",
        ["Select run args to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los argumentos de run a quitar (barra espaciadora para seleccionar, Enter para confirmar)",

        // Argumentos de publish
        ["Select a project to manage publish arguments"] = "Elegí un proyecto para gestionar argumentos de publish",
        ["Select a subproject in '{0}' to manage publish arguments"] =
            "Elegí un subproyecto en '{0}' para gestionar argumentos de publish",
        ["Add Publish Arg"] = "Agregar Argumento de Publish",
        ["Remove Publish Args"] = "Quitar Argumentos de Publish",
        ["Toggle Zip Publish Output"] = "Alternar Salida Zip de Publish",
        ["Select publish args to remove (use space-bar to select, Enter to confirm)"] =
            "Elegí los argumentos de publish a quitar (barra espaciadora para seleccionar, Enter para confirmar)"
    };

    public static void SetLanguage(string language) => _currentLanguage = language;

    public static string T(string english) =>
        _currentLanguage == "es" && EnToEs.TryGetValue(english, out var translated) ? translated : english;
}
