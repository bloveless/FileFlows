﻿using FileFlows.Server.Controllers;
using FileFlows.Server.Helpers;
using FileFlows.Shared.Helpers;
using FileFlows.Shared.Models;

namespace FileFlows.Server.Workers
{
    public class TelemetryReporter: Worker
    {
        public TelemetryReporter() : base(ScheduleType.Hourly, 1)
        {
            Trigger();
        }

        protected override void Execute()
        {
            if (Settings?.DisableTelemetry == true)
                return; // they have turned it off, dont report anything

            TelemetryData data = new TelemetryData();
            data.ClientUid = Settings.Uid;
            data.Version = Globals.Version;
            var libFiles = new LibraryFileController().GetAll(null).Result;
            data.FilesFailed = libFiles.Where(x => x.Status == FileStatus.ProcessingFailed).Count();
            data.FilesProcessed = libFiles.Where(x => x.Status == FileStatus.Processed).Count();
            var flows = new FlowController().GetAll().Result;
            var dictNodes = new Dictionary<string, int>();
            foreach(var fp in flows?.SelectMany(x => x.Parts)?.ToArray() ?? new FlowPart[] { })
            {
                if (fp == null)
                    continue;
                if (dictNodes.ContainsKey(fp.FlowElementUid))
                    dictNodes[fp.FlowElementUid] = dictNodes[fp.FlowElementUid] + 1;
                else
                    dictNodes.Add(fp.FlowElementUid, 1);
            }
            data.Nodes = dictNodes.Select(x => new TelemetryNode
            {
                Name = x.Key,
                Count = x.Value
            }).ToList();

#if(DEBUG)
            var task = HttpHelper.Post("https://localhost:7197/api/telemetry", data);
#else
            var task = HttpHelper.Post("http://fileflows.com/api/telemetry", data);
            
#endif
            task.Wait();
        }



        public class TelemetryData
        {
            public Guid ClientUid { get; set; }

            public string Version { get; set; }

            public List<TelemetryNode> Nodes { get; set; }

            public int FilesProcessed { get; set; }
            public int FilesFailed { get; set; }
        }

        public class TelemetryNode
        {
            public string Name { get; set; }
            public int Count { get; set; }
        }
    }
}