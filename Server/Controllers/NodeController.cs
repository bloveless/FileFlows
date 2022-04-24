namespace FileFlows.Server.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using FileFlows.Shared.Models;
    using FileFlows.Server.Helpers;
    using System.Runtime.InteropServices;
    using FileFlows.ServerShared.Models;

    /// <summary>
    /// Processing node controller
    /// </summary>
    [Route("/api/node")]
    public class NodeController : ControllerStore<ProcessingNode>
    {
        /// <summary>
        /// Gets a list of all processing nodes in the system
        /// </summary>
        /// <returns>a list of processing node</returns>
        [HttpGet]
        public async Task<IEnumerable<ProcessingNode>> GetAll()
        {
            var nodes = (await GetDataList()).OrderBy(x => x.Address == Globals.FileFlowsServer ? 0 : 1).ThenBy(x => x.Name);
            var internalNode = nodes.Where(x => x.Address == Globals.FileFlowsServer).FirstOrDefault();
            if(internalNode != null && internalNode.OperatingSystem == Shared.OperatingSystemType.Unknown)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    internalNode.OperatingSystem = Shared.OperatingSystemType.Windows;
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    internalNode.OperatingSystem = Shared.OperatingSystemType.Mac;
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    internalNode.OperatingSystem = Shared.OperatingSystemType.Linux;
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                    internalNode.OperatingSystem = Shared.OperatingSystemType.Linux;

                if(internalNode.OperatingSystem != Shared.OperatingSystemType.Unknown)
                {
                    await Update(internalNode);
                }
            }
#if (DEBUG)
            // set this to linux so we can test the full UI
            if (internalNode != null)
                internalNode.OperatingSystem = Shared.OperatingSystemType.Linux;
#endif
            return nodes;
        }

        /// <summary>
        /// Get processing node
        /// </summary>
        /// <param name="uid">The UID of the processing node</param>
        /// <returns>The processing node instance</returns>
        [HttpGet("{uid}")]
        public Task<ProcessingNode> Get(Guid uid) => GetByUid(uid);

        /// <summary>
        /// Saves a processing node
        /// </summary>
        /// <param name="node">The node to save</param>
        /// <returns>The saved instance</returns>
        [HttpPost]
        public async Task<ProcessingNode> Save([FromBody] ProcessingNode node)
        {
            // see if we are updating the internal node
            if(node.Address  == Globals.FileFlowsServer)
            {
                var internalNode = (await GetAll()).Where(x => x.Address == Globals.FileFlowsServer).FirstOrDefault();
                if(internalNode != null)
                {
                    internalNode.Schedule = node.Schedule;
                    internalNode.FlowRunners = node.FlowRunners;
                    internalNode.Enabled = node.Enabled;
                    internalNode.TempPath = node.TempPath;
                    internalNode.DontChangeOwner = node.DontChangeOwner;
                    internalNode.DontSetPermissions = node.DontSetPermissions;
                    internalNode.Permissions = node.Permissions;
                    return await Update(internalNode, checkDuplicateName: true);
                }
                else
                {
                    // internal but doesnt exist
                    node.Address = Globals.FileFlowsServer;
                    node.Name = Globals.FileFlowsServer;
                    node.Mappings = null; // no mappings for internal
                }
            }
            return await Update(node, checkDuplicateName: true);
        }

        /// <summary>
        /// Delete processing nodes from the system
        /// </summary>
        /// <param name="model">A reference model containing UIDs to delete</param>
        /// <returns>an awaited task</returns>
        [HttpDelete]
        public async Task Delete([FromBody] ReferenceModel model)
        {
            var internalNode = (await this.GetAll()).Where(x => x.Address == Globals.FileFlowsServer).FirstOrDefault()?.Uid ?? Guid.Empty;
            if (model.Uids.Contains(internalNode))
                throw new Exception("ErrorMessages.CannotDeleteInternalNode");
            await DeleteAll(model);
        }

        /// <summary>
        /// Set state of a processing node
        /// </summary>
        /// <param name="uid">The UID of the processing node</param>
        /// <param name="enable">Whether or not this node is enabled and will process files</param>
        /// <returns>an awaited task</returns>
        [HttpPut("state/{uid}")]
        public async Task<ProcessingNode> SetState([FromRoute] Guid uid, [FromQuery] bool? enable)
        {
            var node = await GetByUid(uid);
            if (node == null)
                throw new Exception("Node not found.");
            if (enable != null)
            {
                node.Enabled = enable.Value;
                await DbManager.Update(node);
            }
            return node;
        }

        /// <summary>
        /// Get processing node by address
        /// </summary>
        /// <param name="address">The address</param>
        /// <returns>If found, the processing node</returns>
        [HttpGet("by-address/{address}")]
        public async Task<ProcessingNode> GetByAddress([FromRoute] string address)
        {
            if (address == "INTERNAL_NODE")
                return await GetServerNode();

            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));

            address = address.Trim();
            var data = await GetData();
            var node = data.Where(x => x.Value.Address.ToLower() == address.ToLower()).Select(x => x.Value).FirstOrDefault();
            if (node == null)
                return node;

            node.SignalrUrl = "flow";
            return node;
        }

        /// <summary>
        /// Register a processing node.  If already registered will return existing instance
        /// </summary>
        /// <param name="address">The address of the processing node</param>
        /// <returns>The processing node instance</returns>
        [HttpGet("register")]
        public async Task<ProcessingNode> Register([FromQuery]string address)
        {
            if(string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));

            address = address.Trim();
            var data = await GetData();
            var existing = data.Where(x => x.Value.Address.ToLower() == address.ToLower()).Select(x => x.Value).FirstOrDefault();
            if (existing != null)
            {
                existing.SignalrUrl = "flow";
                return existing;
            }
            var settings = await new SettingsController().Get();
            // doesnt exist, register a new node.
            var tools = await new ToolController().GetAll();
            bool isSystem = address == Globals.FileFlowsServer;
            var result = await Update(new ProcessingNode
            {
                Name = address,
                Address = address,
                Enabled = isSystem, // default to disabled so they have to configure it first
                FlowRunners = 1,
                Schedule = new string('1', 672),
                Mappings = isSystem  ? null : tools.Select(x => new
                    KeyValuePair<string, string>(x.Path, "")
                ).ToList()
            });
            result.SignalrUrl = "flow";
            return result;
        }


        /// <summary>
        /// Register a processing node.  If already registered will return existing instance
        /// </summary>
        /// <param name="model">The register model containing information about the processing node being registered</param>
        /// <returns>The processing node instance</returns>
        [HttpPost("register")]
        public async Task<ProcessingNode> RegisterPost([FromBody] RegisterModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.Address))
                throw new ArgumentNullException(nameof(model.Address));
            if (string.IsNullOrWhiteSpace(model?.TempPath))
                throw new ArgumentNullException(nameof(model.TempPath));

            var address = model.Address.Trim();
            var data = await GetData();
            var existing = data.Where(x => x.Value.Address.ToLower() == address.ToLower()).Select(x => x.Value).FirstOrDefault();
            if (existing != null)
            {
                if(existing.FlowRunners != model.FlowRunners || existing.TempPath != model.TempPath || existing.Enabled != model.Enabled)
                {
                    existing.FlowRunners = model.FlowRunners;
                    existing.TempPath = model.TempPath;
                    existing.Enabled = model.Enabled;
                    existing.OperatingSystem = model.OperatingSystem;
                    await Update(existing);
                }
                existing.SignalrUrl = "flow";
                return existing;
            }
            var settings = await new SettingsController().Get();
            // doesnt exist, register a new node.
            var tools = await new ToolController().GetAll();

            if(model.Mappings?.Any() == true)
            {
                var ffmpegTool = tools.Where(x => x.Name.ToLower() == "ffmpeg").FirstOrDefault();
                if (ffmpegTool != null)
                {
                    // update ffmpeg with actual location
                    var mapping = model.Mappings.Where(x => x.Server.ToLower() == "ffmpeg").FirstOrDefault();
                    if(mapping != null)
                    {
                        mapping.Server = ffmpegTool.Path;
                    }
                }
            }

            var result = await Update(new ProcessingNode
            {
                Name = address,
                Address = address,
                Enabled = model.Enabled,
                FlowRunners = model.FlowRunners,
                TempPath = model.TempPath,
                OperatingSystem = model.OperatingSystem,
                Schedule = new string('1', 672),
                Mappings = model.Mappings?.Select(x => new KeyValuePair<string, string>(x.Server, x.Local))?.ToList() ?? tools?.Select(x => new
                   KeyValuePair<string, string>(x.Path, "")
                )?.ToList() ?? new()
            });
            result.SignalrUrl = "flow";
            return result;
        }

        internal async Task<ProcessingNode> GetServerNode()
        {
            var data = await GetData();
            var settings = await new SettingsController().Get();
            var node = data.Where(x => x.Value.Name == Globals.FileFlowsServer).Select(x => x.Value).FirstOrDefault();
            if (node == null)
            {
                bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);                
                node = await Update(new ProcessingNode
                {
                    Name = Globals.FileFlowsServer,
                    Address = Globals.FileFlowsServer,
                    Schedule = new string('1', 672),
                    Enabled = true,
                    FlowRunners = 1,
#if (DEBUG)
                    TempPath = windows ? @"d:\videos\temp" : "/temp",
#else
                    TempPath = windows ? Path.Combine(Program.GetAppDirectory(), "Temp") : "/temp",
#endif
                });
            }
            node.SignalrUrl = "flow";
            return node;
        }
    }

}