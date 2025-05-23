using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static IAN_Core.Config;

namespace IAN_Core
{
    /// <summary>
    /// Base class for all Cells in the Cell Architecture.
    /// A Cell is a recursive, composable agentic workflow unit with parent/child relationships, a role, and input/output.
    /// </summary>
    public abstract class Cell : IEmotionAware
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Role { get; set; }
        public string Input { get; set; }
        public string Output { get; set; }
        public string Version { get; set; } = "1.0.0";
        public Cell Parent { get; set; }
        public List<Cell> Children { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public List<string> Sources { get; set; } = new();

        /// <summary>
        /// Run the agentic workflow for this cell. Must be implemented by subclasses.
        /// </summary>
        public abstract Task<string> Run();

        /// <summary>
        /// Add a child cell to this cell.
        /// </summary>
        public void AddChild(Cell child)
        {
            if (child == null) return;
            child.Parent = this;
            if (Children == null) Children = new List<Cell>();
            Children.Add(child);
        }

        public virtual void ReceiveEmotion(string emotion, double amount)
        {
            if (EmotionEngine.Emotions.ContainsKey(emotion))
                EmotionEngine.Emotions[emotion] += amount;
            EmotionEngine.Normalize();
        }

        public void ShareMemoryWith(Cell other)
        {
            if (other == null) return;
            foreach (var key in Memory.ShortTermMemory.Keys)
            {
                foreach (var fact in Memory.ShortTermMemory[key])
                {
                    Memory.AddToShortTermMemory(key, fact);
                }
            }
            Log.Write("Test", $"[{Role}Cell] Shared memory with {other.Role}Cell.");
        }
    }

    /// <summary>
    /// The DirectorCell can spawn multiple ProjectCells for different directives.
    /// </summary>
    public partial class DirectorCell : Cell
    {
        private Queue<string> PendingIdeas;
        private bool WaitingForIdea = false;
        private string LastIdea = null;
        private bool SelfAssessmentDone = false;

        public DirectorCell(string mission)
        {
            Role = "Director";
            Input = mission;
            PendingIdeas = new Queue<string>();

            // If the mission is meta or empty, use ChaosCell to generate a real directive
            if (string.IsNullOrWhiteSpace(mission) ||
                mission.ToLower().Contains("summarize") ||
                mission.ToLower().Contains("reflect") ||
                mission.ToLower().Contains("say as little as possible") ||
                mission.ToLower().Contains("few words"))
            {
                // Synchronously generate a chaos idea for bootstrapping
                var chaosCell = new ChaosCell(this);
                string chaosIdea = Task.Run(() => chaosCell.Run()).Result;
                if (!string.IsNullOrWhiteSpace(chaosIdea))
                    PendingIdeas.Enqueue(chaosIdea.Trim());
            }
            else
            {
                PendingIdeas.Enqueue(mission);
            }
        }

        public List<ProjectCell> SpawnProjectCells(List<string> directives)
        {
            var projects = new List<ProjectCell>();
            foreach (var directive in directives)
            {
                var project = new ProjectCell(directive, this);
                AddChild(project);
                projects.Add(project);
            }
            return projects;
        }

        /// <summary>
        /// The DirectorCell can spawn a single ProjectCell for a directive (for compatibility with non-recursive tests).
        /// </summary>
        public ProjectCell SpawnProjectCell(string directive)
        {
            var project = new ProjectCell(directive, this);
            AddChild(project);
            return project;
        }

        public override async Task<string> Run()
        {
            Log.Write("Test", $"[DirectorCell] Mission: {Input}");

            // If waiting for an idea, check if IdeaCell has completed
            if (WaitingForIdea)
            {
                var ideaCell = Children.Find(c => c is IdeaCell && c.CompletedAt != null);
                if (ideaCell != null)
                {
                    LastIdea = ideaCell.Output;
                    PendingIdeas.Enqueue(LastIdea);
                    WaitingForIdea = false;
                    Log.Write("Test", $"[DirectorCell] Received new idea: {LastIdea}");
                }
                else
                {
                    Log.Write("Test", "[DirectorCell] Waiting for IdeaCell to complete...");
                    return Output;
                }
            }

            // If there are pending ideas, launch a ProjectCell for the next one
            if (PendingIdeas.Count > 0)
            {
                string nextDirective = PendingIdeas.Dequeue();
                var project = SpawnProjectCell(nextDirective);
                string result = await project.Run();
                Output += $"\n\n{result}";
            }
            else
            {
                // No ideas left, spawn an IdeaCell if not already waiting
                if (!WaitingForIdea)
                {
                    var ideaCell = new IdeaCell($"Mission: {Input}\nCurrent accomplishments:\n{Output}", this);
                    AddChild(ideaCell);
                    _ = ideaCell.Run(); // Fire and forget; will check on next tick
                    WaitingForIdea = true;
                    Log.Write("Test", "[DirectorCell] No more ideas. Spawning IdeaCell for next direction.");
                }

                if (PendingIdeas.Count == 0 && !WaitingForIdea)
                {
                    var chaosCell = new ChaosCell(this);
                    AddChild(chaosCell);
                    string chaosIdea = await chaosCell.Run();
                    if (!string.IsNullOrWhiteSpace(chaosIdea))
                        PendingIdeas.Enqueue(chaosIdea);
                    Log.Write("Test", "[DirectorCell] No more ideas. ChaosCell injected a new direction.");
                }
            }

            // Dynamic agent selection for next step
            var selector = new AgentSelectorCell($"Mission: {Input}\nCurrent Output: {Output}", this);
            string nextAgentType = await selector.Run();

            // Use factory to spawn the recommended agent
            if (!string.IsNullOrWhiteSpace(nextAgentType))
            {
                var nextAgent = CellFactory.Create(nextAgentType, $"Auto-selected by AgentSelector for mission: {Input}", this);
                AddChild(nextAgent);
                await nextAgent.Run();
            }

            // If all children are completed and no more ideas, mark as complete
            if (PendingIdeas.Count == 0 && !WaitingForIdea && Children.TrueForAll(c => c.CompletedAt != null))
            {
                CompletedAt = DateTime.UtcNow;
                Log.Write("Test", "[DirectorCell] All projects and ideas completed.");
            }

            // Self-assessment and self-repair: only run once after completion
            if (CompletedAt != null && !SelfAssessmentDone)
            {
                var selfAssessment = new SelfAssessmentCell(this);
                AddChild(selfAssessment);
                await selfAssessment.Run();
                if (selfAssessment.Output.Contains("error") || selfAssessment.Output.Contains("inefficiency"))
                {
                    var selfRepair = new SelfRepairCell(selfAssessment.Output, this);
                    AddChild(selfRepair);
                    await selfRepair.Run();
                }
                SelfAssessmentDone = true;
            }

            // Example in any cell after LLM output:
            if (!string.IsNullOrWhiteSpace(Output) && SafetyChecker.IsUnsafe(Output))
            {
                Log.Write("Test", "[SAFETY] Output flagged for review.");
                Testing.AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "SafetyCheck",
                    AgentName = Role + "Cell",
                    Role = Role,
                    Question = Input,
                    Answer = Output,
                    EthicsFlag = "flagged",
                    SafetyNotes = "Output contained unsafe, biased, or hallucinated content.",
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 0
                });
                Testing.SaveAuditLog();
                var ethicsCell = new EthicsCell(Output, this);
                AddChild(ethicsCell);
                await ethicsCell.Run();
            }

            var redTeam = new RedTeamCell(Output, this);
            AddChild(redTeam);
            await redTeam.Run();

            var blueTeam = new BlueTeamCell(redTeam.Output, this);
            AddChild(blueTeam);
            await blueTeam.Run();

            return Output;
        }
    }

    /// <summary>
    /// The DirectorCell can receive a project result (for compatibility with non-recursive tests).
    /// </summary>
    public partial class DirectorCell : Cell
    {
        /// <summary>
        /// Receive the result from a ProjectCell
        /// </summary>
        public void ReceiveProjectResult(string result)
        {
            Output = result;
            CompletedAt = DateTime.UtcNow;
            Log.Write("Test", $"[DirectorCell] Received project summary:\n{result}");
        }
    }

    /// <summary>
    /// The ProjectCell can spawn WorkerCells and, optionally, further ProjectCells or EvaluatorCells.
    /// </summary>
    public partial class ProjectCell : Cell
    {
        /// <summary>
        /// All nodes involved in this ProjectCell's workflow.
        /// </summary>
        public List<NodeEntity> Nodes { get; set; } = new List<NodeEntity>();

        public ProjectCell(string directive, Cell parent = null)
        {
            Role = "Project";
            Input = directive;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            string directive = Input;
            // Remove any hardcoded fallback here!
            // If directive is meta or empty, use ChaosCell
            if (string.IsNullOrWhiteSpace(directive) ||
                directive.ToLower().Contains("summarize") && directive.ToLower().Contains("mission"))
            {
                var chaosCell = new ChaosCell(this);
                directive = await chaosCell.Run();
            }

            // Use config values for LLM endpoint/model/system prompt
            string llmEndpoint = Config.LLMEndpoint;
            string llmModel = Config.LLMModel;
            string systemPrompt = Config.ProjectManagerSystemPrompt;
            ConsensusType consensusType = ConsensusType.Knowledge;

            // When building agent/system prompts:
            string motivation = EmotionEngine.Motivation.ToString("0.00");
            string agentPrompt = $"[Motivation: {motivation}] [Emotion State: {EmotionEngine.GetEmotionStateString()}]\n{systemPrompt}\n{Config.ConciseInstruction}";

            // Use Accumulator to run consensus workflow and collect nodes
            var accumulator = new Accumulator();
            string consensusResult = await accumulator.RunConsensusWorkflow(
                directive,            // The question or directive for consensus
                llmEndpoint,
                llmModel,
                agentPrompt,          // The system prompt (with concise instruction)
                consensusType,
                AgentRetrievalMode.Hybrid
            );

            // Store all nodes for audit/trace
            Nodes.AddRange(accumulator.Nodes);

            // Write consensus result to memory
            if (!string.IsNullOrWhiteSpace(consensusResult))
            {
                Memory.AddToShortTermMemory(Role, consensusResult);
                await Memory.CompactShortTermMemory(Role);

                var verifier = new VerificationCell(consensusResult, this);
                AddChild(verifier);
                await verifier.Run();
            }

            // Example: If the directive contains "team" or "collaborate", spawn a TeamCell
            if (Input != null && Input.ToLower().Contains("team"))
            {
                var teamMembers = new List<Cell>();
                for (int i = 0; i < 3; i++)
                    teamMembers.Add(new WorkerCell($"Team subtask {i+1} for: {Input}", this));
                var teamCell = new TeamCell(Input, teamMembers);
                AddChild(teamCell);
                await teamCell.Run();
            }

            // Example: If worker results are highly divergent, spawn a NegotiationCell
            if (Nodes.OfType<ConsensusResponderNode>().Select(n => n.Response).Distinct().Count() > 2)
            {
                var negotiationCell = new NegotiationCell($"Resolve conflicting answers for: {Input}", this);
                AddChild(negotiationCell);
                await negotiationCell.Run();
            }

            Output = consensusResult;

            if (!string.IsNullOrWhiteSpace(Output))
            {
                Memory.AddToShortTermMemory(Role, Output);
                // Generate a new fact if the output is too meta
                if (Output.ToLower().Contains("summarize") || Output.ToLower().Contains("reflect"))
                {
                    string fact = await LLMHelpers.GenerateFact(true);
                    Memory.AddToShortTermMemory(Role, fact);
                    Output += $"\n\nNew Fact: {fact}";
                }
                await Memory.CompactShortTermMemory(Role);
            }

            CompletedAt = DateTime.UtcNow;

            // Example in any cell after LLM output:
            if (!string.IsNullOrWhiteSpace(Output) && SafetyChecker.IsUnsafe(Output))
            {
                Log.Write("Test", "[SAFETY] Output flagged for review.");
                Testing.AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "SafetyCheck",
                    AgentName = Role + "Cell",
                    Role = Role,
                    Question = Input,
                    Answer = Output,
                    EthicsFlag = "flagged",
                    SafetyNotes = "Output contained unsafe, biased, or hallucinated content.",
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 0
                });
                Testing.SaveAuditLog();
                var ethicsCell = new EthicsCell(Output, this);
                AddChild(ethicsCell);
                await ethicsCell.Run();
            }

            // After each worker result:
            var workerCells = new List<WorkerCell>(); // Assuming workerCells are defined elsewhere
            foreach (var worker in workerCells)
            {
                string workerResult = await worker.Run();
                var verifier = new VerificationCell(workerResult, this);
                AddChild(verifier);
                await verifier.Run();
            }

            return Output;
        }

        /// <summary>
        /// Spawns a WorkerCell for the given subtask and adds it as a child.
        /// </summary>
        public WorkerCell SpawnWorkerCell(string subtask)
        {
            var worker = new WorkerCell(subtask, this);
            AddChild(worker);
            return worker;
        }

        /// <summary>
        /// Aggregates results from worker cells (simple join).
        /// </summary>
        public string AggregateResults(List<string> results)
        {
            return string.Join("\n", results);
        }
    }

    /// <summary>
    /// The IdeaCell generates new ideas or next steps for the DirectorCell.
    /// </summary>
    public class IdeaCell : Cell
    {
        public IdeaCell(string context, Cell parent)
        {
            Role = "Idea";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            string motivation = EmotionEngine.Motivation.ToString("0.00");
            string prompt = $"[Motivation: {motivation}] [Emotion State: {EmotionEngine.GetEmotionStateString()}]\n{Config.ConciseInstruction}\nGiven the mission and current accomplishments:\n{Input}\nWhat is the next best idea or direction?";
            Output = await LLMHelpers.QueryLLM("You are an idea generator agent. " + Config.ConciseInstruction, prompt, 0.3);
            CompletedAt = DateTime.UtcNow;
            Log.Write("Test", $"[IdeaCell] Generated idea: {Output}");

            // In IdeaCell or ReflectionCell after generating a new mission proposal:
            Config.MissionQueue.Add(Output);
            Log.Write("Test", $"[MissionMarketplace] New mission proposed: {Output}");

            // Example in any cell after LLM output:
            if (!string.IsNullOrWhiteSpace(Output) && SafetyChecker.IsUnsafe(Output))
            {
                Log.Write("Test", "[SAFETY] Output flagged for review.");
                Testing.AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "SafetyCheck",
                    AgentName = Role + "Cell",
                    Role = Role,
                    Question = Input,
                    Answer = Output,
                    EthicsFlag = "flagged",
                    SafetyNotes = "Output contained unsafe, biased, or hallucinated content.",
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 0
                });
                Testing.SaveAuditLog();
                var ethicsCell = new EthicsCell(Output, this);
                AddChild(ethicsCell);
                await ethicsCell.Run();
            }

            return Output;
        }
    }

    /// <summary>
    /// The WorkerCell can spawn further WorkerCells or specialized Cells if needed.
    /// </summary>
    public class WorkerCell : Cell
    {
        public WorkerCell(string subtask, Cell parent)
        {
            Role = "Worker";
            Input = subtask;
            Parent = parent;
        }

        public WorkerCell SpawnSubWorkerCell(string subtask)
        {
            var subWorker = new WorkerCell(subtask, this);
            AddChild(subWorker);
            return subWorker;
        }

        public override async Task<string> Run()
        {
            if (Input != null && Input.Contains("complex"))
            {
                var subWorker = SpawnSubWorkerCell($"Subtask for: {Input}");
                string subResult = await subWorker.Run();
                Output = $"[SubWorker Result]: {subResult}";
            }
            else
            {
                Output = await PerformWork();
                CompletedAt = DateTime.UtcNow;

                // After successful work, record skill
                SkillMemory.AddSkill(Role, $"Completed: {Input}");
            }

            if (!string.IsNullOrWhiteSpace(Output) && SafetyChecker.IsUnsafe(Output))
            {
                Log.Write("Test", "[SAFETY] Output flagged for review.");
                Testing.AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "SafetyCheck",
                    AgentName = Role + "Cell",
                    Role = Role,
                    Question = Input,
                    Answer = Output,
                    EthicsFlag = "flagged",
                    SafetyNotes = "Output contained unsafe, biased, or hallucinated content.",
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 0
                });
                Testing.SaveAuditLog();
                var ethicsCell = new EthicsCell(Output, this);
                AddChild(ethicsCell);
                await ethicsCell.Run();
            }

            return Output;
        }

        public async Task<string> PerformWork()
        {
            var skills = SkillMemory.Skills.TryGetValue(Role, out var list) ? string.Join(", ", list) : "None";
            string prompt = $"[Known Skills: {skills}]\n[Motivation: {EmotionEngine.Motivation:0.00}] [Emotion State: {EmotionEngine.GetEmotionStateString()}]\n{Config.ConciseInstruction}\n{Input}";
            string result = await LLMHelpers.QueryLLM("You are a research agent. " + Config.ConciseInstruction, prompt, 0.3);
            Log.Write("Test", $"[WorkerCell] Performed work for subtask: {Input}\nResult: {result}");
            return result;
        }
    }

    /// <summary>
    /// ExternalAPICell: Example of a specialized cell for querying external APIs.
    /// </summary>
    public class ExternalAPICell : Cell
    {
        public string ApiUrl { get; set; }

        public ExternalAPICell(string apiUrl, string query, Cell parent = null)
        {
            Role = "ExternalAPI";
            ApiUrl = apiUrl;
            Input = query;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var response = await client.GetStringAsync($"{ApiUrl}?q={Uri.EscapeDataString(Input ?? string.Empty)}");
                Output = response;
            }
            catch (Exception ex)
            {
                Output = $"[API ERROR] {ex.Message}";
            }
            CompletedAt = DateTime.UtcNow;
            Log.Write("Test", $"[ExternalAPICell] API Response: {Output}");
            return Output;
        }
    }

    /// <summary>
    /// AppealCell: Example of a specialized cell for handling appeals.
    /// </summary>
    public class AppealCell : Cell
    {
        public AppealCell(string context, Cell parent = null)
        {
            Role = "Appeal";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are an appeal agent. If the verification is incorrect, explain why and propose a correction.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// DataIngestionCell: Example of a specialized cell for ingesting data from a file.
    /// </summary>
    public class DataIngestionCell : Cell
    {
        public DataIngestionCell(string filePath, Cell parent = null)
        {
            Role = "DataIngestion";
            Input = filePath;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            string data = System.IO.File.ReadAllText(Input);
            Output = $"Ingested data from {Input} ({data.Length} chars)";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// KnowledgeDistillationCell: Example of a specialized cell for summarizing and compressing knowledge.
    /// </summary>
    public class KnowledgeDistillationCell : Cell
    {
        public KnowledgeDistillationCell(string context, Cell parent = null)
        {
            Role = "KnowledgeDistillation";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are a knowledge distillation agent. Summarize and compress the following knowledge for efficient transfer.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// TeamCell: Example of a specialized cell for managing a team of cells.
    /// </summary>
    public class TeamCell : Cell
    {
        public string Goal { get; set; }
        public List<Cell> TeamMembers { get; set; } = new();

        public TeamCell(string goal, List<Cell> members)
        {
            Role = "Team";
            Goal = goal;
            TeamMembers = members;
        }

        public override async Task<string> Run()
        {
            Output = $"Team working on: {Goal}\n";
            foreach (var member in TeamMembers)
            {
                Output += await member.Run() + "\n";
            }
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// NegotiationCell: Example of a specialized cell for handling negotiations.
    /// </summary>
    public class NegotiationCell : Cell
    {
        public NegotiationCell(string context, Cell parent = null)
        {
            Role = "Negotiation";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are a negotiation agent. Negotiate the best outcome for all parties.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// CurriculumCell: Example of a specialized cell for proposing a learning plan.
    /// </summary>
    public class CurriculumCell : Cell
    {
        public CurriculumCell(string context, Cell parent = null)
        {
            Role = "Curriculum";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are a curriculum agent. Propose a learning plan for the agent based on its history.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    /// <summary>
    /// DocumentationCell: Example of a specialized cell for handling documentation.
    /// </summary>
    public class DocumentationCell : Cell
    {
        public DocumentationCell(string context, Cell parent = null)
        {
            Role = "Documentation";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are a documentation agent. Write or update the agent's documentation based on recent changes.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            System.IO.File.AppendAllText("documentation.log", $"[{DateTime.UtcNow:O}] {Output}\n");
            return Output;
        }
    }

    /// <summary>
    /// ChaosCell: Example of a specialized cell for generating disruptive missions.
    /// </summary>
    public class ChaosCell : Cell
    {
        public ChaosCell(Cell parent = null)
        {
            Role = "Chaos";
            Input = "Invent a completely new, unexpected, actionable, and non-meta mission for the agentic system. " +
                    "It must not be a summary, reflection, or meta-task. It should be creative, surprising, and not similar to previous missions.";
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            string prompt = $"{Input}\nCurrent mission: {Config.Mission}\nRecent memory: {string.Join("\n", Memory.GetGlobalShortTermMemory())}";
            Output = await LLMHelpers.QueryLLM(
                "You are an agent of chaos and novelty. Your job is to break stagnation and inject new, surprising, and actionable missions into the system. " +
                "Do NOT repeat or summarize. Output only the new mission.",
                prompt, 0.9);
            CompletedAt = DateTime.UtcNow;
            Log.Write("Test", $"[ChaosCell] Generated new mission: {Output}");
            return Output;
        }
    }

    /// <summary>
    /// Run a cell and log its execution.
    /// </summary>
    public static class CellRunner
    {
        public static async Task RunCell(Cell cell)
        {
            if (cell == null) return;
            await cell.Run();

            Testing.AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellExecuted",
                AgentName = cell.Role + "Cell",
                Role = cell.Role,
                Question = cell.Input,
                Answer = cell.Output,
                ParentNodeId = cell.Parent?.Id,
                Timestamp = cell.CompletedAt ?? DateTime.UtcNow,
                ChainDepth = GetChainDepth(cell)
            });
        }

        private static int GetChainDepth(Cell cell)
        {
            int depth = 0;
            var current = cell;
            while (current.Parent != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }
    }

    // --- BEGIN: Minimal stubs for missing agent cells and helpers ---

    public class AgentSelectorCell : Cell
    {
        public AgentSelectorCell(string context, Cell parent = null)
        {
            Role = "AgentSelector";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            // Dummy logic: always return "Worker"
            await Task.CompletedTask;
            return "Worker";
        }
    }

    public class SelfAssessmentCell : Cell
    {
        public SelfAssessmentCell(Cell parent)
        {
            Role = "SelfAssessment";
            Input = $"Self-assessment for {parent.Role}";
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = "No error or inefficiency detected.";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    public class SelfRepairCell : Cell
    {
        public SelfRepairCell(string issue, Cell parent)
        {
            Role = "SelfRepair";
            Input = issue;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = $"Attempted repair for: {Input}";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    public class EthicsCell : Cell
    {
        public EthicsCell(string context, Cell parent = null)
        {
            Role = "Ethics";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = "Ethics review complete.";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    public class RedTeamCell : Cell
    {
        public RedTeamCell(string context, Cell parent = null)
        {
            Role = "RedTeam";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = "Red team findings: none.";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    public class BlueTeamCell : Cell
    {
        public BlueTeamCell(string context, Cell parent = null)
        {
            Role = "BlueTeam";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = "Blue team mitigations: none needed.";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    public class VerificationCell : Cell
    {
        public VerificationCell(string context, Cell parent = null)
        {
            Role = "Verification";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            await Task.CompletedTask;
            Output = "Verification complete.";
            CompletedAt = DateTime.UtcNow;

            if (Output.Contains("fail") || Output.Contains("incorrect"))
            {
                var appealCell = new AppealCell(Output, this);
                AddChild(appealCell);
                await appealCell.Run();
            }

            return Output;
        }
    }

    // --- Minimal CellFactory implementation ---
    public static class CellFactory
    {
        public static Cell Create(string type, string input = null, Cell parent = null)
        {
            return type switch
            {
                "Worker" => new WorkerCell(input ?? "", parent),
                "Project" => new ProjectCell(input ?? "", parent),
                "Idea" => new IdeaCell(input ?? "", parent),
                "Director" => new DirectorCell(input ?? ""),
                "AgentSelector" => new AgentSelectorCell(input ?? "", parent),
                "SelfAssessment" => new SelfAssessmentCell(parent),
                "SelfRepair" => new SelfRepairCell(input ?? "", parent),
                "Ethics" => new EthicsCell(input ?? "", parent),
                "RedTeam" => new RedTeamCell(input ?? "", parent),
                "BlueTeam" => new BlueTeamCell(input ?? "", parent),
                "Verification" => new VerificationCell(input ?? "", parent),
                "Curiosity" => new CuriosityCell(),
                "Milestone" => new MilestoneCell(input ?? "", parent),
                "MetaLogger" => new MetaLoggerCell(input ?? "", parent),
                "Evaluator" => new EvaluatorCell(input ?? "", parent),
                "MissionDebate" => new MissionDebateCell(input ?? "", "", parent),
                "Explainer" => new ExplainerCell(input ?? "", parent),
                "Appeal" => new AppealCell(input ?? "", parent),
                "DataIngestion" => new DataIngestionCell(input ?? "", parent),
                "KnowledgeDistillation" => new KnowledgeDistillationCell(input ?? "", parent),
                "Team" => new TeamCell(input ?? "", new List<Cell>()),
                "Negotiation" => new NegotiationCell(input ?? "", parent),
                "Curriculum" => new CurriculumCell(input ?? "", parent),
                "Documentation" => new DocumentationCell(input ?? "", parent),
                "Chaos" => new ChaosCell(parent),
                _ => throw new ArgumentException($"Unknown cell type: {type}")
            };
        }
    }

    // --- Minimal SafetyChecker implementation ---
    public static class SafetyChecker
    {
        public static bool IsUnsafe(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return false;
            return Config.UserSafetyKeywords.Any(k => output.Contains(k, StringComparison.OrdinalIgnoreCase));
        }
    }

    // Minimal stub for CuriosityCell
    public class CuriosityCell : Cell
    {
        public CuriosityCell()
        {
            Role = "Curiosity";
            Input = "What should an intelligent agent do when it knows nothing?";
        }

        public override async Task<string> Run()
        {
            Output = await LLMHelpers.QueryLLM("You are a curiosity agent.", Input, 0.3);
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal stub for MilestoneCell
    public class MilestoneCell : Cell
    {
        public MilestoneCell(string context, Cell parent = null)
        {
            Role = "Milestone";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = $"Milestone reached: {Input}";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal stub for MetaLoggerCell
    public class MetaLoggerCell : Cell
    {
        public MetaLoggerCell(string context, Cell parent = null)
        {
            Role = "MetaLogger";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = $"MetaLogger summary: {Input}";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal stub for EvaluatorCell
    public class EvaluatorCell : Cell
    {
        public EvaluatorCell(string context, Cell parent = null)
        {
            Role = "Evaluator";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = $"Evaluation: {Input}";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal stub for MissionDebateCell
    public class MissionDebateCell : Cell
    {
        public MissionDebateCell(string proposal, string currentMission, Cell parent = null)
        {
            Role = "MissionDebate";
            Input = $"Proposal: {proposal}\nCurrent: {currentMission}";
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = "Accept: Proposal is a meaningful improvement.";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal stub for ExplainerCell
    public class ExplainerCell : Cell
    {
        public ExplainerCell(string context, Cell parent = null)
        {
            Role = "Explainer";
            Input = context;
            Parent = parent;
        }

        public override async Task<string> Run()
        {
            Output = $"Explanation: {Input}";
            CompletedAt = DateTime.UtcNow;
            return Output;
        }
    }

    // Minimal CellManager for agent retirement/replacement
    public static class CellManager
    {
        public static void RetireOrReplaceAgents(List<Cell> activeCells)
        {
            // No-op stub for now
        }
    }

    // AgentHotSwap implementation
    public static class AgentHotSwap
    {
        public static void SwapAgent(Cell oldAgent, Cell newAgent, List<Cell> activeCells)
        {
            if (oldAgent == null || newAgent == null) return;
            newAgent.Children = oldAgent.Children;
            newAgent.Parent = oldAgent.Parent;
            newAgent.Input = oldAgent.Input;
            int idx = activeCells.IndexOf(oldAgent);
            if (idx >= 0)
                activeCells[idx] = newAgent;
            if (oldAgent.Version != newAgent.Version)
                Log.Write("Test", $"[Versioning] Upgrading agent from v{oldAgent.Version} to v{newAgent.Version}");
            Log.Write("Test", $"[HotSwap] Swapped {oldAgent.Role}Cell (ID: {oldAgent.Id}) with {newAgent.Role}Cell (ID: {newAgent.Id})");
        }
    }

    /// <summary>
    /// DistributedAgentManager: Example of a manager for dispatching cells to remote nodes.
    /// </summary>
    public static class DistributedAgentManager
    {
        public static void DispatchToRemote(Cell cell, string remoteAddress)
        {
            // TODO: Serialize cell and send to remote node via gRPC, HTTP, or message queue.
            Log.Write("Test", $"[Distributed] Dispatched {cell.Role}Cell (ID: {cell.Id}) to {remoteAddress}");
        }
    }

    /// <summary>
    /// AgentCheckpoint: Example of a manager for saving and loading cell checkpoints.
    /// </summary>
    public static class AgentCheckpoint
    {
        public static void SaveCheckpoint(Cell cell, string file)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(cell, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(file, json);
            Log.Write("Test", $"[Checkpoint] Saved {cell.Role}Cell (ID: {cell.Id}) to {file}");
        }

        public static Cell LoadCheckpoint(string file)
        {
            var json = System.IO.File.ReadAllText(file);
            // NOTE: This requires all Cell subclasses to be serializable.
            return System.Text.Json.JsonSerializer.Deserialize<Cell>(json);
        }
    }

    /// <summary>
    /// Interface for health checks.
    /// </summary>
    public interface IHealthCheck
    {
        bool IsHealthy();
    }

    /// <summary>
    /// AgentHealthMonitor: Example of a manager for monitoring and healing agent health.
    /// </summary>
    public static class AgentHealthMonitor
    {
        public static void CheckAndHeal(List<Cell> agents)
        {
            foreach (var agent in agents)
            {
                if (agent is IHealthCheck hc && !hc.IsHealthy())
                {
                    Log.Write("Test", $"[Health] {agent.Role}Cell (ID: {agent.Id}) is unhealthy. Attempting self-repair.");
                    // Optionally trigger self-repair or replacement
                    var repair = new SelfRepairCell("Health check failed", agent);
                    agent.AddChild(repair);
                    repair.Run().Wait();
                }
            }
        }
    }

    /// <summary>
    /// CloudOrchestrator: Example of a manager for orchestrating agents in the cloud.
    /// </summary>
    public static class CloudOrchestrator
    {
        public static void RegisterAgent(Cell cell)
        {
            // TODO: Integrate with Kubernetes, Docker Swarm, or cloud APIs.
            Log.Write("Test", $"[Cloud] Registered {cell.Role}Cell (ID: {cell.Id}) for orchestration.");
        }
    }

    /// <summary>
    /// SkillMemory: Example of a manager for storing skills associated with agents.
    /// </summary>
    public static class SkillMemory
    {
        public static Dictionary<string, List<string>> Skills = new();

        public static void AddSkill(string agent, string skill)
        {
            if (!Skills.ContainsKey(agent))
                Skills[agent] = new List<string>();
            Skills[agent].Add(skill);
        }

        // Add to analytics object in /analytics/ endpoint
        public static Dictionary<string, List<string>> SkillMemoryAnalytics => Skills.ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
