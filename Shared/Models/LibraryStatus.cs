namespace FileFlows.Shared.Models;

/// <summary>
/// Library status
/// </summary>
public class LibraryStatus
{
    /// <summary>
    /// Gets or sets the name of the status
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// Gets or sets the file status
    /// </summary>
    public FileStatus Status { get; set; }
    
    /// <summary>
    /// Gets or sets the number of library files in this status
    /// </summary>
    public int Count { get; set; }
}