using System.Reflection;

namespace FileFlows.Node;

class CommandLineOptions 
{
    /// <summary>
    /// Gets or sets the FileFlows server
    /// </summary>
    [CommandLineArg("The FileFlows server", "http://tower:5000/")]
    public string Server { get; set; }
    
    /// <summary>
    /// Gets or sets the name of this Node
    /// </summary>
    [CommandLineArg("The name of this Node", "WindowsNode")]
    public string Name { get; set; }
    
    /// <summary>
    /// Gets or sets the temporary working directory this node will use
    /// </summary>
    [CommandLineArg("The temporary working directory this node will use", "DIR")]
    public string Temp { get; set; }
    
    /// <summary>
    /// Gets or sets the temporary working directory this node will use
    /// </summary>
    [CommandLineArg("If a GUI should be hidden", "true", "no-gui")]
    public bool NoGui { get; set; }

    /// <summary>
    /// Gets or sets if running inside a docker container
    /// </summary>
    [CommandLineArg("If running inside a docker container", "true", "docker", true)]
    public bool Docker { get; set; }

    /// <summary>
    /// Parses the command line arguments
    /// </summary>
    /// <param name="args">the command line arguments</param>
    /// <returns>the command line options</returns>
    public static CommandLineOptions Parse(string[] args)
    {
        CommandLineOptions options = new();
        if (args?.Any() != true)
            return options;

        var props = typeof(CommandLineOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            var ca = prop.GetCustomAttribute<CommandLineArgAttribute>();
            if (ca == null)
                continue;
            string key = ca.Command?.EmptyAsNull() ?? prop.Name.ToLower();
            string strValue = GetArgValue("--" + key);
            if (string.IsNullOrEmpty(strValue))
                continue;
            
            if(prop.PropertyType == typeof(bool))
                prop.SetValue(options, strValue.ToLower() == "true" || strValue == "1");
            else 
                prop.SetValue(options, strValue);
        }

        return options;

        string GetArgValue(string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].ToLower() == key)
                    return args[i + 1];
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Prints the help to the command line
    /// </summary>
    public static void PrintHelp()
    {
        Console.WriteLine("FileFlows Node Version:" + Globals.Version);
        Console.WriteLine("");
        var props = typeof(CommandLineOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            var ca = prop.GetCustomAttribute<CommandLineArgAttribute>();
            if (ca == null || ca.Hidden)
                continue;
            
            var key = ca.Command?.EmptyAsNull() ?? prop.Name.ToLower();
            var example = ca.Example;
            if (example == "DIR")
                example = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? @"C:\FileFlows\Temp" : "/mnt/temp";
            string value = prop.PropertyType == typeof(bool) ? "true|false" : "value";
            Console.WriteLine($"--{key} [{value}]");
            Console.WriteLine($"\t{ca.Description}");
            Console.WriteLine($"\teg --{key} {example}");
        }
    }
}

class CommandLineArgAttribute:Attribute 
{
    public string Description { get; set; }
    public string Command { get; set; }
    public string Example { get; set; }
    public bool Hidden { get; set; }

    public CommandLineArgAttribute(string description, string example, string command = null, bool hidden = false)
    {
        this.Description = description;
        this.Example = example;
        this.Command = command ?? string.Empty;
        this.Hidden = hidden;
    }
}