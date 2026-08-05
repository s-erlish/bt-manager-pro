using System;
using System.Reflection;

namespace BluetoothManagerPro.Infrastructure;

/// <summary>Identity shown in the footer of the settings tab.</summary>
public static class AppInfo
{
    public const string Author = "by virdur";

    /// <summary>
    /// Version as written in the project file. Read from the assembly rather than
    /// duplicated as a constant, so the number in the UI can never drift from the build.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    public static string Footer => $"{Version} · {Author}";

    private static string ReadVersion()
    {
        string? informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit>" when the build knows the source revision.
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        Version? assembly = typeof(AppInfo).Assembly.GetName().Version;
        return assembly is null ? "1.0.0" : $"{assembly.Major}.{assembly.Minor}.{assembly.Build}";
    }
}
