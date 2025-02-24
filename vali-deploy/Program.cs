using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using vali_deploy.Managers;
using vali_deploy.Models;
using vali_deploy.Utils;

try
{
    // Iniciar el menú principal
    await MenuManager.StartAsync();
}
catch (Exception ex)
{
    // Manejar errores inesperados
    AnsiConsole.MarkupLine($"[red] :cross_mark: Fatal error: {ex.Message}[/]");
    AnsiConsole.MarkupLine($"[red] :books: StackTrace: {ex.StackTrace}[/]");
    AnsiConsole.MarkupLine(" :hand_with_fingers_splayed: Press any key to continue...");
    Console.ReadKey();
}