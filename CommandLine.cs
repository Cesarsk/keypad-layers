using System.Diagnostics;

namespace KeypadLayers;

internal sealed record CommandLine(string Executable, string Arguments)
{
    public static CommandLine Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 32767 || command.Contains('\0') || command.Contains('\r') || command.Contains('\n'))
            throw new ArgumentException("Enter a single command line (up to 32767 characters).");
        command = command.Trim();
        if (command[0] == '"')
        {
            int end = command.IndexOf('"', 1);
            if (end < 0 || end == 1 || (end + 1 < command.Length && !char.IsWhiteSpace(command[end + 1]))) throw new ArgumentException("Check the quotes around the executable path.");
            return new(command[1..end], command[(end + 1)..].TrimStart());
        }
        int separator = command.IndexOfAny([' ', '\t']);
        return separator < 0 ? new(command, "") : new(command[..separator], command[(separator + 1)..].TrimStart());
    }

    public static ProcessStartInfo StartInfo(Mapping mapping)
    {
        var command = Parse(mapping.Value);
        string working = Environment.ExpandEnvironmentVariables(mapping.WorkingDirectory.Trim());
        if (working.Length > 0 && !Directory.Exists(working)) throw new DirectoryNotFoundException("Working directory not found: " + working);
        return new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(command.Executable),
            Arguments = command.Arguments,
            WorkingDirectory = working,
            UseShellExecute = false,
            CreateNoWindow = mapping.RunHidden,
            WindowStyle = mapping.RunHidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };
    }
}
