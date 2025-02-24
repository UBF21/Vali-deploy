namespace vali_deploy.Utils;

public static class Util
{
    
    public static Spectre.Console.Color GetRandomColor()
    {
        var colors = new List<Spectre.Console.Color>
        {
            Spectre.Console.Color.Red,
            Spectre.Console.Color.Green,
            Spectre.Console.Color.Blue,
            Spectre.Console.Color.Yellow,
            Spectre.Console.Color.Purple,
            Spectre.Console.Color.Orange1
        };

        var random = new Random();
        return colors[random.Next(colors.Count)];
    }
}