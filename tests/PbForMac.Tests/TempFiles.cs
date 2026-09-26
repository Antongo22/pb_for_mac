namespace PbForMac.Tests;

/// <summary>Временная папка для тестовых файлов, удаляется после теста.</summary>
public sealed class TempFiles : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "pbformac-tests-" + Guid.NewGuid().ToString("N"));

    public TempFiles() => System.IO.Directory.CreateDirectory(Directory);

    public string Write(string name, string content)
    {
        var path = PathOf(name);
        File.WriteAllText(path, content);
        return path;
    }

    public string PathOf(string name) => Path.Combine(Directory, name);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
