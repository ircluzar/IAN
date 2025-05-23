using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static IAN_Core.Config;

namespace IAN_Core
{
    /// <summary>
    /// The Accumulator class is a workflow pipeline that automates the orchestration of nodes to accomplish tasks.
    /// Supports spawning sub-agents for consensus and different consensus types.
    /// Now supports advanced memory: short-term, long-term, and hybrid memory injection.
    /// </summary>
    public class Accumulator
    {
        private const string LogTag = "Accumulator";

        /// <summary>
        /// All nodes involved in this workflow instance.
        /// </summary>
        public List<NodeEntity> Nodes { get; set; } = new List<NodeEntity>();

        /// <summary>
        /// Number of sub-agents to spawn for consensus.
        /// </summary>
        public int SubAgentCount => Config.SubAgentCount;

        public Accumulator() { }

        /// <summary>
        /// Runs a consensus workflow:
        /// - Spawns an orchestrator node.
        /// - Spawns sub-agents (responders) to answer the question.
        /// - Outputs each sub-agent's answer.
        /// - Orchestrator uses LLM to determine consensus from sub-agent answers.
        /// - Supports advanced memory: short-term, long-term, and hybrid memory injection.
        /// </summary>
        public async Task<string> RunConsensusWorkflow(
            string question,
            string llmEndpoint,
            string llmModel,
            string systemPrompt,
            ConsensusType consensusType,
            AgentRetrievalMode retrievalMode = AgentRetrievalMode.ModelOnly)
        {
            Log.Write(LogTag, "=== Starting Consensus Workflow ===");
            Log.Write(LogTag, $"Question: {question}");
            Log.Write(LogTag, $"Consensus Type: {consensusType}");
            Log.Write(LogTag, $"System Prompt: {systemPrompt}");
            Log.Write(LogTag, $"LLM Endpoint: {llmEndpoint}");
            Log.Write(LogTag, $"LLM Model: {llmModel}");
            Log.Write(LogTag, $"Spawning {Config.SubAgentCount} sub-agents...\n");

            // --- ADVANCED MEMORY INJECTION ---
            // For Hybrid/MemoryOnly, dynamically load relevant memory (short-term and/or long-term)
            string nodeType = "Consensus"; // You may want to make this dynamic per workflow
            List<string> relevantShortTerm = new();
            List<string> relevantLongTerm = new();
            string hybridMemory = "";

            if (retrievalMode == AgentRetrievalMode.Hybrid || retrievalMode == AgentRetrievalMode.MemoryOnly)
            {
                // 1. Search short-term memory for relevance
                relevantShortTerm = Memory.SearchMemory(nodeType, question, maxResults: 5);

                // 2. If not enough, load relevant long-term memory fragments
                if (relevantShortTerm.Count < 3)
                {
                    Memory.LoadRelevantLongTermToShortTerm(nodeType, question, maxToLoad: 5 - relevantShortTerm.Count);
                    relevantShortTerm = Memory.GetShortTermMemory(nodeType);
                }

                // 3. Compose hybrid memory block for prompt injection
                if (relevantShortTerm.Count > 0)
                    hybridMemory = string.Join("\n", relevantShortTerm);
                else
                {
                    // Fallback: use any available long-term memory
                    relevantLongTerm = Memory.GetLongTermMemory(nodeType);
                    if (relevantLongTerm.Count > 0)
                        hybridMemory = string.Join("\n", relevantLongTerm.GetRange(0, Math.Min(5, relevantLongTerm.Count)));
                }
            }

            // 1. Create orchestrator node
            var orchestrator = new ConsensusOrchestratorNode
            {
                Id = Guid.NewGuid(),
                Name = "Consensus Orchestrator",
                Type = "Orchestrator",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
                Description = "Orchestrates consensus among responders",
                Question = question
            };
            Nodes.Add(orchestrator);

            // 2. Spawn sub-agents and collect their answers
            var subAgentAnswers = new List<string>();
            for (int i = 0; i < Config.SubAgentCount; i++)
            {
                Log.Write(LogTag, $"Spawning Sub-Agent {i + 1}...");
                var responder = new ConsensusResponderNode
                {
                    Id = Guid.NewGuid(),
                    Name = $"Consensus Responder {i + 1}",
                    Type = "Responder",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    Description = $"Sub-agent responder #{i + 1}",
                    OrchestratorId = orchestrator.Id,
                    ReceivedQuestion = question,
                    RetrievalMode = retrievalMode
                };
                Nodes.Add(responder);

                Log.Write(LogTag, $"Created node: {responder.Name} (ID: {responder.Id})");

                // Advanced: Dynamic agent memory injection and prompt diversity for hybrid/memory mode
                string agentPrompt = AgentPromptHelpers.BuildAgentPrompt(systemPrompt, retrievalMode, hybridMemory, i);

                var messages = new List<Intel.Message>
                {
                    new Intel.Message { role = "system", content = $"{agentPrompt}\nYou are agent #{i + 1}." },
                    new Intel.Message { role = "user", content = question }
                };

                Log.Write("Sub-Agent", $"[{responder.Name}] SYSTEM PROMPT: \"{messages[0].content}\"");
                Log.Write("Sub-Agent", $"[{responder.Name}] QUESTION: \"{messages[1].content}\"");
                Log.Write(LogTag, $"Sub-Agent {i + 1} sending query to LLM...");

                // Decide model for each sub-agent (could use agent role, question, etc.)
                (string endpoint, string model) = ModelSelector.AssignModel(
                    agentRole: "ConsensusResponder",
                    taskType: "consensus",
                    context: question
                );

                // Fallback logic (pseudo, implement health check as needed)
                if (!ModelHealthManager.IsAvailable(endpoint))
                {
                    endpoint = Config.MidModelAddress;
                    model = "mid-8b";
                }

                try
                {
                    string answer = await Intel.QueryLLMAsync(endpoint, model, messages, 0.7, -1, false);
                    responder.Response = answer;
                    subAgentAnswers.Add(answer);
                    Log.Write("Sub-Agent", $"[{responder.Name}] OUTPUT: \"{answer}\"\n");
                    if (!string.IsNullOrWhiteSpace(answer))
                        Memory.AddToShortTermMemory(nodeType, answer);
                }
                catch (Exception ex)
                {
                    ModelHealthManager.MarkUnavailable(endpoint);
                    Log.Write(LogTag, $"Model endpoint {endpoint} failed: {ex.Message}. Falling back to Mid model.");
                    endpoint = Config.MidModelAddress;
                    model = "mid-8b";
                    string answer = await Intel.QueryLLMAsync(endpoint, model, messages, 0.7, -1, false);
                    responder.Response = answer;
                    subAgentAnswers.Add(answer);
                }
            }

            // After consensus is reached, check for answer diversity:
            if (subAgentAnswers.Count > 1 && new HashSet<string>(subAgentAnswers).Count == subAgentAnswers.Count)
            {
                Log.Write(LogTag, "Warning: All sub-agent answers are unique. Possible ambiguity or lack of consensus.", ConsoleColor.Yellow);
            }

            // 3. Orchestrator asks LLM to determine consensus from sub-agent answers
            Log.Write(LogTag, "All sub-agent answers collected. Determining consensus...");

            string consensusPrompt = AgentPromptHelpers.BuildConsensusPrompt(consensusType, subAgentAnswers);

            var consensusMessages = new List<Intel.Message>
            {
                new Intel.Message { role = "system", content = "You are a consensus judge agent. Output ONLY the answer, not any explanation, not any mention of consensus, and not any process." },
                new Intel.Message { role = "user", content = consensusPrompt }
            };

            Log.Write("Consensus", $"SYSTEM PROMPT: \"{consensusMessages[0].content}\"");
            Log.Write("Consensus", $"QUESTION: \"{consensusMessages[1].content}\"");
            Log.Write(LogTag, "Orchestrator sending consensus prompt to LLM...");
            string consensusResult = await Intel.QueryLLMAsync(llmEndpoint, llmModel, consensusMessages, 0.7, -1, false);
            orchestrator.ConsensusResult = consensusResult;
            orchestrator.IsConsensusReached = true;

            Log.Write("Consensus", $"OUTPUT: \"{consensusResult}\"\n");

            if (!string.IsNullOrWhiteSpace(consensusResult))
            {
                Memory.AddToShortTermMemory(nodeType, consensusResult);
                Memory.AddToGlobalShortTermMemory(consensusResult);

                // If the consensus is meta, force a new fact
                if (consensusResult.ToLower().Contains("summarize") || consensusResult.ToLower().Contains("reflect"))
                {
                    string fact = await LLMHelpers.GenerateFact(true);
                    Memory.AddToShortTermMemory(nodeType, fact);
                    Memory.AddToGlobalShortTermMemory(fact);
                }

                await Memory.CompactShortTermMemory(nodeType);
            }

            Log.Write(LogTag, "=== Consensus Workflow Complete ===\n");

            // Optionally: Compact short-term memory after workflow (to avoid bloat)
            await Memory.CompactShortTermMemory(nodeType);

            return consensusResult;
        }

        /// <summary>
        /// Truncates a string to a maximum length, adding "..." if truncated.
        /// </summary>
        private static string Truncate(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Length <= maxLength ? input : input.Substring(0, maxLength - 3) + "...";
        }
    }

    /// <summary>
    /// Extension methods for lists.
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>
        /// Joins a list of strings with newlines for display or LLM prompts.
        /// </summary>
        public static string JoinWithNewlines(this List<string> list)
        {
            return string.Join("\n", list);
        }
    }

    /// <summary>
    /// Enum for agent retrieval modes.
    /// </summary>
    public enum AgentRetrievalMode
    {
        ModelOnly,
        MemoryOnly,
        Hybrid
    }

    /// <summary>
    /// Helper methods for building agent prompts.
    /// </summary>
    public static class AgentPromptHelpers
    {
        public static string BuildAgentPrompt(
            string systemPrompt,
            AgentRetrievalMode retrievalMode,
            string hybridMemory = "",
            int agentIndex = 0)
        {
            string agentPrompt = systemPrompt;
            if (retrievalMode == AgentRetrievalMode.MemoryOnly)
            {
                agentPrompt += "\n(You MUST ignore your own knowledge and only use the provided memory.)";
                if (!string.IsNullOrWhiteSpace(hybridMemory))
                    agentPrompt += $"\nMEMORY:\n{hybridMemory}";
            }
            else if (retrievalMode == AgentRetrievalMode.Hybrid)
            {
                if (!string.IsNullOrWhiteSpace(hybridMemory))
                    agentPrompt = string.Format(Config.HybridRetrievalSystemPrompt, hybridMemory);
                agentPrompt += $"\n(You should combine the provided memory with your own knowledge. Agent #{agentIndex + 1} may use a different reasoning path.)";
                var diversityHints = new[]
                {
                    "Be concise.",
                    "Be detailed.",
                    "Cite the memory if you use it.",
                    "If unsure, explain your reasoning.",
                    "Prioritize accuracy over creativity."
                };
                agentPrompt += $"\nHint: {diversityHints[agentIndex % diversityHints.Length]}";
            }
            else
            {
                agentPrompt += "\n(You may use your own knowledge and reasoning.)";
            }
            return agentPrompt;
        }

        public static string BuildConsensusPrompt(ConsensusType consensusType, List<string> subAgentAnswers)
        {
            return consensusType switch
            {
                ConsensusType.Word =>
                    "Given ONLY the following answers, output the single word that is the answer. Do NOT mention consensus, do NOT explain, do NOT mention the process. Output ONLY the answer word, nothing else:\n" +
                    subAgentAnswers.JoinWithNewlines(),
                ConsensusType.Number =>
                    "Given ONLY the following answers, output the number that is the answer. Do NOT mention consensus, do NOT explain, do NOT mention the process. Output ONLY the answer number, nothing else:\n" +
                    subAgentAnswers.JoinWithNewlines(),
                ConsensusType.Bool =>
                    "Given ONLY the following answers, output either 'True' or 'False' as the answer. Do NOT mention consensus, do NOT explain, do NOT mention the process. Output ONLY 'True' or 'False', nothing else:\n" +
                    subAgentAnswers.JoinWithNewlines(),
                ConsensusType.Knowledge =>
                    "Given ONLY the following answers, output the answer as a single sentence. Do NOT mention consensus, do NOT explain, do NOT mention the process. Output ONLY the answer sentence, nothing else:\n" +
                    subAgentAnswers.JoinWithNewlines(),
                _ =>
                    "Given ONLY the following answers, output the answer. Do NOT mention consensus, do NOT explain, do NOT mention the process. Output ONLY the answer, nothing else:\n" +
                    subAgentAnswers.JoinWithNewlines()
            };
        }
    }

    /// <summary>
    /// Prototype for testing cell abstraction.
    /// </summary>
    public class TestCellAbstractionPrototype
    {
        public List<Log.AuditLogEntry> AuditLog { get; set; } = new List<Log.AuditLogEntry>();

        public void CreateCell(Cell cell)
        {
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellCreated",
                AgentName = cell.Role + "Cell",
                Role = cell.Role,
                Question = cell.Input,
                Answer = null,
                ParentNodeId = cell.Parent?.Id,
                Timestamp = cell.CreatedAt,
                ChainDepth = GetChainDepth(cell)
            });
        }

        public void RunCell(Cell cell)
        {
            cell.Run();

            AuditLog.Add(new Log.AuditLogEntry
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

        private int GetChainDepth(Cell cell)
        {
            int depth = 0;
            var current = cell.Parent;
            while (current != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }

        public string GetMemorySummary()
        {
            string memorySummary = string.Join("\n", Memory.GetShortTermMemory("Global"));
            return memorySummary;
        }
    }
}