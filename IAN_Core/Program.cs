using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using IAN_Core;

class Program
{
    static Dictionary<string, List<string>> lastSnapshot = null;

    static async Task Main(string[] args)
    {
        // Clear the Copilot console log at startup
        System.IO.File.WriteAllText("copilot_console.log", string.Empty);

        try
        {
            Task.Run(async () =>
            {
                using var listener = new System.Net.HttpListener();
                listener.Prefixes.Add("http://localhost:8080/status/");
                listener.Prefixes.Add("http://localhost:8080/analytics/");
                listener.Prefixes.Add("http://localhost:8080/create-agent/");
                listener.Prefixes.Add("http://localhost:8080/hotswap-agent/");
                listener.Prefixes.Add("http://localhost:8080/ingest-data/");
                listener.Prefixes.Add("http://localhost:8080/override-model/");
                listener.Prefixes.Add("http://localhost:8080/edit-mission/");
                listener.Prefixes.Add("http://localhost:8080/replay-scenario/");
                listener.Start();
                while (true)
                {
                    var context = listener.GetContext();
                    var response = context.Response;
                    if (context.Request.Url.AbsolutePath == "/status/")
                    {
                        string status = $"Active Agents: {Machine.ActiveCells.Count}\nMission: {Config.Mission}";
                        var buffer = System.Text.Encoding.UTF8.GetBytes(status);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/analytics/")
                    {
                        var analytics = new
                        {
                            ShortTermMemory = Memory.ShortTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                            LongTermMemory = Memory.LongTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                            ActiveAgents = Machine.ActiveCells.Select(a => new {
                                a.Role, a.Id, a.Input, a.Output, a.CreatedAt, a.CompletedAt,
                                Children = a.Children.Select(c => new { c.Role, c.Id }).ToList()
                            }).ToList(),
                            SkillMemory = IAN_Core.SkillMemory.SkillMemoryAnalytics,
                            Timestamp = DateTime.UtcNow
                        };
                        var json = System.Text.Json.JsonSerializer.Serialize(analytics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/create-agent/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        string type = data["type"];
                        string input = data["input"];
                        var agent = CellFactory.Create(type, input);
                        Machine.ActiveCells.Add(agent);
                        var responseMsg = $"Agent {type} created with input: {input}";
                        var buffer = System.Text.Encoding.UTF8.GetBytes(responseMsg);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/hotswap-agent/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        Guid oldId = Guid.Parse(data["oldId"]);
                        string newType = data["newType"];
                        var oldAgent = Machine.ActiveCells.FirstOrDefault(c => c.Id == oldId);
                        var newAgent = CellFactory.Create(newType, oldAgent?.Input, oldAgent?.Parent);
                        AgentHotSwap.SwapAgent(oldAgent, newAgent, Machine.ActiveCells);
                        var responseMsg = $"Hot-swapped agent {oldId} to new type {newType}.";
                        var buffer = System.Text.Encoding.UTF8.GetBytes(responseMsg);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/ingest-data/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        string filePath = data["filePath"];
                        var ingestionCell = new DataIngestionCell(filePath);
                        string result = await ingestionCell.Run();
                        var buffer = System.Text.Encoding.UTF8.GetBytes(result);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/documentation/")
                    {
                        // Assume documentation outputs are stored in a static list or file
                        var docs = System.IO.File.Exists("documentation.log")
                            ? System.IO.File.ReadAllText("documentation.log")
                            : "No documentation available.";
                        var buffer = System.Text.Encoding.UTF8.GetBytes(docs);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/pause-agent/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        Guid agentId = Guid.Parse(data["id"]);
                        var agent = Machine.ActiveCells.FirstOrDefault(a => a.Id == agentId);
                        if (agent != null) agent.CompletedAt = DateTime.UtcNow; // Mark as paused/completed
                        var buffer = System.Text.Encoding.UTF8.GetBytes("Agent paused.");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/retire-agent/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        Guid agentId = Guid.Parse(data["id"]);
                        var agent = Machine.ActiveCells.FirstOrDefault(a => a.Id == agentId);
                        if (agent != null) agent.CompletedAt = DateTime.UtcNow;
                        var buffer = System.Text.Encoding.UTF8.GetBytes("Agent retired.");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/copilot-log/")
                    {
                        var lines = System.IO.File.Exists("copilot_console.log")
                            ? System.IO.File.ReadLines("copilot_console.log").Reverse().Take(100).Reverse()
                            : new List<string> { "No log available." };
                        var logText = string.Join("\n", lines);
                        var buffer = System.Text.Encoding.UTF8.GetBytes(logText);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/override-model/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        Guid agentId = Guid.Parse(data["id"]);
                        string model = data["model"];
                        Config.AgentModelOverrides[agentId] = model;
                        var buffer = System.Text.Encoding.UTF8.GetBytes($"Model for agent {agentId} overridden to {model}.");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/set-mission/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        string newMission = data["mission"];
                        Config.Mission = newMission;
                        Config.SaveMission();
                        var buffer = System.Text.Encoding.UTF8.GetBytes($"Mission updated to: {newMission}");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/edit-mission/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        string newMission = data["mission"];
                        Config.Mission = newMission;
                        Config.SaveMission();
                        var buffer = System.Text.Encoding.UTF8.GetBytes($"Mission updated to: {newMission}");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/replay-scenario/" && context.Request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(context.Request.InputStream);
                        var body = reader.ReadToEnd();
                        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);
                        int milestoneIdx = int.Parse(data["milestone"]);
                        Memory.ImportSystemState($"milestone_{milestoneIdx}.json");
                        var buffer = System.Text.Encoding.UTF8.GetBytes($"Replayed system state from milestone {milestoneIdx}.");
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/emotion-state/")
                    {
                        var emotionState = EmotionEngine.Emotions;
                        var json = System.Text.Json.JsonSerializer.Serialize(emotionState, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/memory-analytics/")
                    {
                        var analytics = new
                        {
                            ShortTermMemory = Memory.ShortTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                            LongTermMemory = Memory.LongTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                            Timestamp = DateTime.UtcNow
                        };
                        var json = System.Text.Json.JsonSerializer.Serialize(analytics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/agent-hierarchy/")
                    {
                        var hierarchy = Machine.ActiveCells.Select(cell => new {
                            cell.Role,
                            cell.Id,
                            cell.Parent,
                            Children = cell.Children.Select(c => new { c.Role, c.Id }).ToList()
                        }).ToList();
                        var json = System.Text.Json.JsonSerializer.Serialize(hierarchy, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    else if (context.Request.Url.AbsolutePath == "/mission-history/")
                    {
                        var history = Machine.GetMissionHistory(); // Implement this to return the mission history list
                        var json = System.Text.Json.JsonSerializer.Serialize(history, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }
                    response.OutputStream.Close();
                }
            });

            if (args.Length > 0 && args[0] == "snapshot")
            {
                lastSnapshot = Memory.SnapshotShortTermMemory();
                Console.WriteLine("Memory snapshot taken.");
                return;
            }
            if (args.Length > 0 && args[0] == "rollback")
            {
                if (lastSnapshot != null)
                {
                    Memory.RestoreShortTermMemory(lastSnapshot);
                    Console.WriteLine("Memory rolled back to last snapshot.");
                }
                else
                {
                    Console.WriteLine("No snapshot available.");
                }
                return;
            }
            if (args.Length > 1 && args[0] == "mission")
            {
                Config.Mission = string.Join(" ", args.Skip(1));
                Config.SaveMission();
                Console.WriteLine($"Mission set to: {Config.Mission}");
                return;
            }
            if (args.Length > 1 && args[0] == "explain")
            {
                var metaLogger = new MetaLoggerCell(string.Join(" ", args.Skip(1)));
                string explanation = await metaLogger.Run();
                Console.WriteLine($"MetaLogger Explanation:\n{explanation}");
                return;
            }
            if (args.Length > 1 && args[0] == "replay")
            {
                int milestoneIdx = int.Parse(args[1]);
                // Load system state from milestone (implement state saving per milestone)
                Memory.ImportSystemState($"milestone_{milestoneIdx}.json");
                Console.WriteLine($"Replayed system state from milestone {milestoneIdx}.");
                return;
            }
            if (args.Length > 0 && args[0] == "benchmark")
            {
                await Testing.RunAllBenchmarks();
                return;
            }
            if (args.Length > 1 && args[0] == "add-safety-keyword")
            {
                Config.UserSafetyKeywords.Add(args[1]);
                Console.WriteLine($"Added safety keyword: {args[1]}");
                return;
            }
            if (args.Length > 0 && args[0] == "export-milestones")
            {
                Machine.ExportMilestoneTimeline();
                Console.WriteLine("Milestone timeline exported to milestone_timeline.csv");
                return;
            }
            if (args.Length > 0 && args[0] == "export-analytics")
            {
                Memory.ExportAnalyticsJson();
                Console.WriteLine("Analytics exported to analytics.json");
                return;
            }
            if (args.Length > 1 && args[0] == "ingest")
            {
                var ingestionCell = new DataIngestionCell(args[1]);
                string result = await ingestionCell.Run();
                Console.WriteLine(result);
                return;
            }
            if (args.Length > 2 && args[0] == "hotswap")
            {
                Guid oldId = Guid.Parse(args[1]);
                string newType = args[2];
                var oldAgent = Machine.ActiveCells.FirstOrDefault(c => c.Id == oldId);
                var newAgent = CellFactory.Create(newType, oldAgent?.Input, oldAgent?.Parent);
                AgentHotSwap.SwapAgent(oldAgent, newAgent, Machine.ActiveCells);
                Console.WriteLine($"Hot-swapped agent {oldId} to new type {newType}.");
                return;
            }
            if (args.Length > 0 && args[0] == "reload-config")
            {
                Config.LoadMission();
                Console.WriteLine("Configuration reloaded.");
                return;
            }
            if (args.Length > 0 && args[0] == "prune-memory")
            {
                foreach (var key in Memory.ShortTermMemory.Keys.ToList())
                    Memory.ShortTermMemory[key] = Memory.ShortTermMemory[key].Take(3).ToList();
                foreach (var key in Memory.LongTermMemory.Keys.ToList())
                    Memory.LongTermMemory[key] = Memory.LongTermMemory[key].Take(5).ToList();
                Console.WriteLine("Memory pruned.");
                return;
            }
            if (args.Length > 0 && args[0] == "help")
            {
                Console.WriteLine("Available commands:");
                Console.WriteLine("  snapshot                - Take a memory snapshot");
                Console.WriteLine("  rollback                - Roll back to last memory snapshot");
                Console.WriteLine("  mission <text>          - Set the current mission");
                Console.WriteLine("  explain <text>          - Get a MetaLogger explanation");
                Console.WriteLine("  replay <idx>            - Replay system state from milestone");
                Console.WriteLine("  benchmark               - Run all benchmarks");
                Console.WriteLine("  add-safety-keyword <kw> - Add a safety keyword");
                Console.WriteLine("  export-milestones       - Export milestone timeline to CSV");
                Console.WriteLine("  export-analytics        - Export analytics to JSON");
                Console.WriteLine("  ingest <file>           - Ingest structured data (CSV/JSON)");
                Console.WriteLine("  hotswap <oldId> <type>  - Hot-swap agent by ID to new type");
                Console.WriteLine("  reload-config           - Reload configuration");
                return;
            }
            if (args.Length > 0 && args[0] == "init-open-source")
            {
                System.IO.File.WriteAllText("LICENSE", "MIT License\n\nCopyright (c) 2025...");
                System.IO.File.WriteAllText("CONTRIBUTING.md", "# Contribution Guide\n\n- Fork the repo\n- Submit PRs\n- Follow code of conduct\n");
                Console.WriteLine("Open source files generated.");
                return;
            }

            // --- Run the Machine workflow instead ---
            await Machine.Run();
            Memory.SaveAll();
        }
        catch (Exception ex)
        {
            var errorBlock = $"\n=== COPILOT ERROR START ===\n{ex}\nStackTrace:\n{ex.StackTrace}\n=== COPILOT ERROR END ===\n";
            System.IO.File.AppendAllText("copilot_console.log", errorBlock);
            Console.WriteLine(errorBlock);
            throw;
        }
    }
}