﻿namespace FileFlows.Server.Workers;

using FileFlows.Server.Controllers;
using FileFlows.ServerShared.Workers;
using FileFlows.Shared;
using FileFlows.Shared.Helpers;

/// <summary>
/// A worker that automatically updates FileFlows
/// </summary>
public class AutoUpdater : UpdaterWorker
{
    private DateTime LastCheckedOnline = DateTime.MinValue;
    private int LastCheckedOnlineIntervalMinutes = 60; // 60 minutes

    private static bool DevTest;

    public AutoUpdater() : base("server-upgrade", 60)
    {
        DevTest = Environment.GetEnvironmentVariable("DevTest") == "1";
        if(DevTest)
            SetSchedule(ScheduleType.Minute, 1);
    }

    protected override void QuitApplication()
    {
        WorkerManager.StopWorkers();
        Environment.Exit(99);
    }
    
    protected override bool GetAutoUpdatesEnabled()
    {
        var settings = new SettingsController().Get().Result;
        return settings?.AutoUpdate == true;
    }

    protected override bool CanUpdate()
    {
        var workers = new WorkerController(null).GetAll();
        return workers?.Any() != true;
    }

    protected override string DownloadUpdateBinary()
    {
        var result = GetLatestOnlineVersion();
        if (result.updateAvailable == false)
            return string.Empty;
        
        Version onlineVersion = result.onlineVersion;

        string updateDirectory = Path.Combine(DirectoryHelper.BaseDirectory, "Updates");

        string file = Path.Combine(updateDirectory, $"FileFlows-{onlineVersion}.zip");
        if (File.Exists(file))
        {
            Logger.Instance.ILog("AutoUpdater: Update already downloaded: " + file);
            return string.Empty;
        }

        if (Directory.Exists(updateDirectory) == false)
            Directory.CreateDirectory(updateDirectory);

        Logger.Instance.ILog("AutoUpdater: Downloading update: " + onlineVersion);
        DownloadFile(file).Wait();
        return file;
    }

    /// <summary>
    /// Gets the latest version available online
    /// </summary>
    /// <returns>The latest version available online</returns>
    public static (bool updateAvailable, Version onlineVersion) GetLatestOnlineVersion()
    {
        try
        {
            string url = "https://fileflows.com/api/telemetry/latest-version";
            if (DevTest)
                url += "?devtest=true";
            var result = HttpHelper.Get<string>(url, noLog: true).Result;
            if (result.Success == false)
            {
                Logger.Instance.ILog("AutoUpdater: Failed to retrieve online version");
                return (false, new Version(0,0,0,0));
            }

            Version current = Version.Parse(Globals.Version);
            Version? onlineVersion;
            if (Version.TryParse(result.Data, out onlineVersion) == false)
            {
                Logger.Instance.ILog("AutoUpdater: Failed to parse online version: " + result.Data);
                return (false, new Version(0, 0, 0, 0));
            }
            if (current >= onlineVersion)
            {
                Logger.Instance.ILog($"AutoUpdater: Current version '{current}' newer or same as online version '{onlineVersion}'");
                return (false, onlineVersion);
            }
            return (true, onlineVersion);
        }
        catch (Exception ex)
        {
            Logger.Instance.ELog("AutoUpdater: Failed checking online version: " + ex.Message);
            return (false, new Version(0, 0, 0, 0));
        }
    }
    
    private async Task DownloadFile(string file)
    {
        string url = "https://fileflows.com/downloads/zip?ts=" + DateTime.Now.Ticks;
        if (DevTest)
            url += "&devtest=true";

        using HttpClient httpClient = new();
        
        using HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        using Stream streamToReadFrom = await response.Content.ReadAsStreamAsync(); 
        using Stream streamToWriteTo = File.Open(file, FileMode.Create); 
        await streamToReadFrom.CopyToAsync(streamToWriteTo);
    }
}
