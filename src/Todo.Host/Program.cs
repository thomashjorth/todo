namespace Todo.Host;

public static class Program
{
    // No window: from 2.0.0 the app is a web server and the window is the browser's. --headless only
    // existed to switch Photino off, and a bare "--headless" is not valid command-line configuration,
    // so it is gone from the scripts and the README as well.
    public static void Main(string[] args) => TodoHost.Build(args).Run();
}
