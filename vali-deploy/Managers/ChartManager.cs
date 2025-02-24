using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;

namespace vali_deploy.Managers;

public static class ChartManager
{
     public static BarChart CreateBarChart(Dictionary<string, Project
     > projects)
            {
                var barChart = new BarChart()
                    .Width(100)
                    .Label("[green bold underline]Number of Projects[/]")
                    .CenterLabel();
    
                foreach (var project in projects)
                {
                    string projectName = project.Key; // Nombre del proyecto
                    int actionCount = project.Value.SubProjects.Count; // Número de acciones
    
                    // Agregar el proyecto al gráfico de barras
                    barChart.AddItem(projectName, actionCount, Util.GetRandomColor());
                }
    
                return barChart;
            }
}