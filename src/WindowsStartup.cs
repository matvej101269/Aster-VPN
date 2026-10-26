using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Xml.Linq;

namespace AsterVPN;

public static class WindowsStartup
{
    private static string TaskName => "AsterVPN-" + WindowsIdentity.GetCurrent().User!.Value;

    public static string BuildXml(string executable, string userSid) => new XDocument(
        new XElement(XName.Get("Task", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
            new XAttribute("version", "1.2"),
            Element("Triggers", Element("LogonTrigger", Element("Enabled", "true"), Element("UserId", userSid), Element("Delay", "PT10S"))),
            Element("Principals", new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                Element("UserId", userSid), Element("LogonType", "InteractiveToken"), Element("RunLevel", "HighestAvailable"))),
            Element("Settings", Element("MultipleInstancesPolicy", "IgnoreNew"), Element("DisallowStartIfOnBatteries", "false"),
                Element("StopIfGoingOnBatteries", "false"), Element("ExecutionTimeLimit", "PT0S"), Element("StartWhenAvailable", "true")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"), Element("Exec",
                Element("Command", executable), Element("Arguments", "--startup"), Element("WorkingDirectory", Path.GetDirectoryName(executable)!)))))
        .ToString();

    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    private static XElement Element(string name, params object[] content) => new(Ns + name, content);

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            // Query first: deleting an already absent task is an idempotent operation.
            if (Run("/Query", "/TN", TaskName) == 0 && Run("/Delete", "/TN", TaskName, "/F") != 0)
                throw new IOException("Не удалось отключить автозапуск в планировщике Windows.");
            return;
        }
        var file = Path.Combine(Path.GetTempPath(), "aster-startup-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(file, BuildXml(Environment.ProcessPath ?? throw new IOException("Не найден путь клиента."), WindowsIdentity.GetCurrent().User!.Value), System.Text.Encoding.Unicode);
            if (Run("/Create", "/TN", TaskName, "/XML", file, "/F") != 0)
                throw new IOException("Не удалось включить автозапуск в планировщике Windows.");
        }
        finally { File.Delete(file); }
    }

    private static int Run(params string[] args)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Не удалось открыть планировщик Windows.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(); throw new IOException("Планировщик Windows не ответил."); }
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        return process.ExitCode;
    }
}
