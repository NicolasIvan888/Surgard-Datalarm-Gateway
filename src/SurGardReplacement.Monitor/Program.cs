namespace SurGardReplacement.Monitor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var dataDirectory = ReadDataDirectory(args);
        if (args.Any(arg => string.Equals(arg, "--blacklist-admin", StringComparison.OrdinalIgnoreCase)))
            Application.Run(new BlacklistForm(dataDirectory));
        else
            Application.Run(new MainForm(dataDirectory));
    }

    internal static string ReadDataDirectory(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--data-dir", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(args[index + 1]);
        }

        return Path.GetFullPath(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "SurGardReplacement"));
    }
}
