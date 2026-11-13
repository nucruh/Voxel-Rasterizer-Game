// Wrapper for file shit
public static class FileRead
{
    public static string ReadAll(string path)
    {
        return File.ReadAllText(path);
    }
}