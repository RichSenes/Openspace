namespace Openspace.Infrastructure;

/// <summary>Files with the person's keys: written whole beside their place, readable by the person only, then put in place.</summary>
internal static class PrivateFiles
{
    public static void Write(string file, string text)
    {
        var folder = Path.GetDirectoryName(file)!;
        var temporary = file + ".new";
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(temporary, text);
            File.Move(temporary, file, overwrite: true);
            return;
        }

        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using (var stream = new FileStream(temporary, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(text);
        }

        File.Move(temporary, file, overwrite: true);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
