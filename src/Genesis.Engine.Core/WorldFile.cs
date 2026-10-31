using System.Text;
namespace Genesis.Engine.Core;

/// <summary>Writes a complete temporary snapshot before replacing the previous save on the same volume.</summary>
public static class WorldFile
{
    public static void Save(Simulation simulation, string path)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] data = Encoding.UTF8.GetBytes(simulation.SaveJson());
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Simulation Load(string path) => Simulation.LoadJson(File.ReadAllText(path));
}
