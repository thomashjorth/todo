using Todo.Core.Persistence;
using Todo.Tunnel;

namespace Todo.Host;

public static class Program
{
    // The variable SetPassword.cmd hands the password over in. An environment variable rather than
    // stdin or an argument: PowerShell 5.1 pipes to a native command as ASCII and .NET reads a
    // redirected stdin in the console's OEM code page, so a password with æøå would be hashed as
    // something else than the browser sends; an argument would land in the process list.
    private const string PasswordVariable = "TODO_PASSWORD";

    // No window: from 2.0.0 the app is a web server and the window is the browser's. --headless only
    // existed to switch Photino off, and a bare "--headless" is not valid command-line configuration,
    // so it is gone from the scripts and the README as well.
    public static int Main(string[] args)
    {
        if (args is ["hash-password"])
        {
            return HashPassword();
        }

        if (args is ["export-db", var target])
        {
            return ExportDatabase(target, TodoDatabase.DefaultPath);
        }

        if (args is ["export-db", var exportTarget, var source])
        {
            return ExportDatabase(exportTarget, source);
        }

        TodoHost.Build(args).Run();

        return 0;
    }

    /// <summary>Prints the hash alone, so the caller can take the last line as it is.</summary>
    private static int HashPassword()
    {
        var password = Environment.GetEnvironmentVariable(PasswordVariable);

        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine($"Set {PasswordVariable} to the password first; an empty one is refused.");

            return 1;
        }

        Console.WriteLine(PasswordHash.Create(password));

        return 0;
    }

    /// <summary>
    /// The database as one file for the NAS, without the tokens 1.16.0 stored. The source is read, never
    /// changed, so it is safe while the app is open.
    /// </summary>
    private static int ExportDatabase(string target, string source)
    {
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"There is no database at '{source}'.");

            return 1;
        }

        try
        {
            DatabaseExport.ExportAsync(source, target).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);

            return 1;
        }

        Console.WriteLine(Path.GetFullPath(target));

        return 0;
    }
}
