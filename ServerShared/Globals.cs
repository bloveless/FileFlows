using System.Runtime.InteropServices;

namespace FileFlows.ServerShared;

/// <summary>
/// Globals variables
/// </summary>
public class Globals
{
    /// <summary>
    /// Gets the version of FileFlows
    /// </summary>
    internal static string Version = "0.5.2.734";

    /// <summary>
    /// Gets if this is running on Windows
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    
    /// <summary>
    /// Gets if this is running on linux
    /// </summary>
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux); 
    
    /// <summary>
    /// Gets if this is running on Mac
    /// </summary>
    public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>
    /// Gets or sets if this node is running inside a docker container
    /// </summary>
    public static bool IsDocker { get; set; }
}