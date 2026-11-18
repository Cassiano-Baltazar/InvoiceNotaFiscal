using System.Text.Json;

namespace CalculoProLabore;

/// <summary>The last values typed, kept in %APPDATA%\CalculoProLabore so they come back next time.</summary>
internal sealed class Preferences
{
    public string Revenue { get; set; } = "";
    public string Rbt12 { get; set; } = "";
    public string FatorR { get; set; } = "";
    public string ProLaboreRate { get; set; } = "28,00";
    public string AccountantFee { get; set; } = "257,00";
    public bool Export { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CalculoProLabore", "preferencias.json");

    public static Preferences Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to remember the values is not a reason to stop the calculator.
        }
    }
}
