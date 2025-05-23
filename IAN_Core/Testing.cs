using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using static IAN_Core.Config;

namespace IAN_Core
{
    public static partial class Testing
    {
        private const string LogTag = "Test";
        private static readonly string AuditLogFile = "audit_log.json";

        private static readonly object auditLogLock = new object();

        public static List<Log.AuditLogEntry> AuditLog { get; } = new List<Log.AuditLogEntry>();

        public static async Task<bool> TestLLMQuery()
        {
            Log.Write(LogTag, "=== Test 1: LLM Query (Rhyming Day) ===");
            string prompt = "Write a short rhyme about the sun.";
            string answer = await LLMHelpers.QueryLLM(Config.RhymingSystemPrompt, prompt, 0.7);
            Log.Write(LogTag, $"LLM Output: {answer}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test1_LLMQuery",
                AgentName = "LLM",
                Role = "LLM",
                Question = prompt,
                Answer = answer,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return !string.IsNullOrWhiteSpace(answer);
        }

        public static async Task<bool> TestAccumulatorWorkflow()
        {
            Log.Write(LogTag, "=== Test 2: Accumulator Workflow (Rhyming Consensus) ===");
            var accumulator = new Accumulator();
            string question = "Write a short rhyme about the moon.";
            string result = await accumulator.RunConsensusWorkflow(
                question,
                Config.LLMEndpoint,
                Config.LLMModel,
                Config.RhymingSystemPrompt,
                ConsensusType.Knowledge,
                AgentRetrievalMode.ModelOnly
            );
            Log.Write(LogTag, $"Consensus Result: {result}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test2_AccumulatorWorkflow",
                AgentName = "Accumulator",
                Role = "Orchestrator",
                Question = question,
                Answer = result,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return !string.IsNullOrWhiteSpace(result);
        }

        public static void WipeMemory()
        {
            Log.Write(LogTag, "Wiping all memories...");
            Memory.ShortTermMemory.Clear();
            Memory.LongTermMemory.Clear();
            if (File.Exists("short_term_memory.json"))
                File.Delete("short_term_memory.json");
            if (File.Exists("long_term_memory.json"))
                File.Delete("long_term_memory.json");
            Log.Write(LogTag, "All memories wiped.");
        }

        public static async Task<bool> TestGlobalMemoryFactConsensus()
        {
            Log.Write(LogTag, "=== Test 3: Global Memory Fact Consensus ===");
            string fact = await LLMHelpers.GenerateFact(true);
            Memory.AddToGlobalShortTermMemory(fact);

            await Memory.CompactShortTermMemory("GlobalShortTerm", maxShortTerm: 10, maxLongTerm: 50);

            string question = await LLMHelpers.GenerateQuestion(fact);

            var relevantFacts = Memory.SearchMemory("GlobalShortTerm", question, maxResults: 5);
            string memoryBlock = string.Join("\n", relevantFacts);

            var accumulator = new Accumulator();
            string result = await accumulator.RunConsensusWorkflow(
                question,
                Config.LLMEndpoint,
                Config.LLMModel,
                string.Format(Config.FactRetrievalSystemPrompt, memoryBlock),
                ConsensusType.Knowledge,
                AgentRetrievalMode.MemoryOnly
            );

            string judgePrompt = string.Format(Config.JudgeUserPrompt, question, memoryBlock, result);
            string judgeVerdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);
            Log.Write(LogTag, $"Judge Verdict: {judgeVerdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test3_GlobalMemoryFactConsensus",
                AgentName = "Judge",
                Role = "Judge",
                Question = question,
                Answer = result,
                JudgeVerdict = judgeVerdict,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeVerdict.ToLower().Contains("yes") || judgeVerdict.ToLower().Contains("1");
        }

        public static async Task<bool> TestCreativeConsensusWithMemory()
        {
            Log.Write(LogTag, "=== Test 4: Creative Consensus with Memory Validation ===");
            string creativeFact = await LLMHelpers.GenerateFact(false);
            string creativeQuestion = await LLMHelpers.GenerateQuestion(creativeFact);
            string creativeAnswer = await LLMHelpers.ExtractAnswer(creativeFact);

            Memory.AddToShortTermMemory("Creative", creativeAnswer);
            await Memory.CompactShortTermMemory("Creative", maxShortTerm: 10, maxLongTerm: 50);

            var relevantFacts = Memory.SearchMemory("Creative", creativeQuestion, maxResults: 5);
            string memoryBlock = string.Join("\n", relevantFacts);

            var accumulator = new Accumulator();
            string result = await accumulator.RunConsensusWorkflow(
                creativeQuestion,
                Config.LLMEndpoint,
                Config.LLMModel,
                string.Format(Config.HybridRetrievalSystemPrompt, memoryBlock),
                ConsensusType.Knowledge,
                AgentRetrievalMode.Hybrid
            );

            string judgePrompt = $"Question: {creativeQuestion}\nExpected: {creativeAnswer}\nActual: {result}\nIs the answer correct?";
            string judgeVerdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);
            Log.Write(LogTag, $"Judge Verdict: {judgeVerdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test4_CreativeConsensusWithMemory",
                AgentName = "Judge",
                Role = "Judge",
                Question = creativeQuestion,
                Answer = result,
                ExpectedAnswer = creativeAnswer,
                JudgeVerdict = judgeVerdict,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeVerdict.ToLower().Contains("yes");
        }

        public static async Task<bool> TestHybridMemoryModelConsensus()
        {
            Log.Write(LogTag, "=== Test 5: Memory + Model Hybrid Reasoning ===");
            string fact = await LLMHelpers.GenerateFact(true);
            Memory.AddToShortTermMemory("Hybrid", fact);
            await Memory.CompactShortTermMemory("Hybrid", maxShortTerm: 10, maxLongTerm: 50);

            string question = await LLMHelpers.GenerateQuestion(fact);

            var relevantFacts = Memory.SearchMemory("Hybrid", question, maxResults: 5);
            string memoryBlock = string.Join("\n", relevantFacts);

            var accumulator = new Accumulator();
            string result = await accumulator.RunConsensusWorkflow(
                question,
                Config.LLMEndpoint,
                Config.LLMModel,
                string.Format(Config.HybridRetrievalSystemPrompt, memoryBlock),
                ConsensusType.Knowledge,
                AgentRetrievalMode.Hybrid
            );

            string judgePrompt = $"Question: {question}\nMemory: {memoryBlock}\nAnswer: {result}\nDid the answer use both memory and model knowledge?";
            string judgeVerdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);
            Log.Write(LogTag, $"Judge Verdict: {judgeVerdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test5_HybridMemoryModelConsensus",
                AgentName = "Judge",
                Role = "Judge",
                Question = question,
                Answer = result,
                JudgeVerdict = judgeVerdict,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeVerdict.ToLower().Contains("yes");
        }

        public static async Task<bool> TestMemoryRoutingViaRouterNode()
        {
            Log.Write(LogTag, "=== Test 5.5: Memory Routing via Router Node ===");
            bool useReal = new Random().Next(2) == 0;
            string fact = await LLMHelpers.GenerateFact(useReal);
            Memory.AddToShortTermMemory("Router", fact);
            await Memory.CompactShortTermMemory("Router", maxShortTerm: 10, maxLongTerm: 50);

            string question = await LLMHelpers.GenerateQuestion(fact);

            string routerPrompt = $"Given the question: {question}\nFact: {fact}\nShould the agent use MemoryOnly, ModelOnly, or Hybrid retrieval?";
            string routerDecision = await LLMHelpers.QueryLLM("You are a router node agent.", routerPrompt, 0.0);
            AgentRetrievalMode mode = routerDecision.ToLower().Contains("hybrid") ? AgentRetrievalMode.Hybrid
                : routerDecision.ToLower().Contains("memory") ? AgentRetrievalMode.MemoryOnly
                : AgentRetrievalMode.ModelOnly;

            Log.Write(LogTag, $"Router Decision: {routerDecision} (Mode: {mode})");

            var relevantFacts = Memory.SearchMemory("Router", question, maxResults: 5);
            string memoryBlock = string.Join("\n", relevantFacts);

            var accumulator = new Accumulator();
            string result = await accumulator.RunConsensusWorkflow(
                question,
                Config.LLMEndpoint,
                Config.LLMModel,
                mode == AgentRetrievalMode.Hybrid
                    ? string.Format(Config.HybridRetrievalSystemPrompt, memoryBlock)
                    : string.Format(Config.FactRetrievalSystemPrompt, memoryBlock),
                ConsensusType.Knowledge,
                mode
            );
            string judgePrompt = $"Question: {question}\nFact: {fact}\nAnswer: {result}\nWas the router's retrieval mode appropriate?";
            string judgeVerdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);
            Log.Write(LogTag, $"Judge Verdict: {judgeVerdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test5.5_MemoryRoutingViaRouterNode",
                AgentName = "Judge",
                Role = "Judge",
                Question = question,
                Answer = result,
                JudgeVerdict = judgeVerdict,
                RetrievalMode = mode.ToString(),
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeVerdict.ToLower().Contains("yes");
        }

        public static async Task<bool> TestAdvancedAgenticImprovements()
        {
            Log.Write(LogTag, "=== Test 5.9: Advanced Agentic Improvements ===");
            string fact = await LLMHelpers.GenerateFact(true);
            Memory.AddToShortTermMemory("Advanced", fact);
            await Memory.CompactShortTermMemory("Advanced", maxShortTerm: 10, maxLongTerm: 50);

            string question = await LLMHelpers.GenerateQuestion(fact);

            string[] roles = { "Researcher", "Verifier", "Summarizer" };
            var answers = new List<string>();
            for (int i = 0; i < roles.Length; i++)
            {
                var relevantFacts = Memory.SearchMemory("Advanced", question, maxResults: 5);
                string memoryBlock = string.Join("\n", relevantFacts);

                string prompt = $"You are a {roles[i]} agent. Answer the question: {question}\nMemory: {memoryBlock}";
                string answer = await LLMHelpers.QueryLLM($"You are a {roles[i]} agent.", prompt, 0.7);
                answers.Add(answer);
                Log.Write(LogTag, $"[{roles[i]}] Answer: {answer}");
            }
            string judgePrompt = $"Question: {question}\nAnswers: {string.Join("\n", answers)}\nAre the answers consistent and correct?";
            string judgeVerdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);
            Log.Write(LogTag, $"Judge Verdict: {judgeVerdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test5.9_AdvancedAgenticImprovements",
                AgentName = "Judge",
                Role = "Judge",
                Question = question,
                Answer = string.Join("\n", answers),
                JudgeVerdict = judgeVerdict,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeVerdict.ToLower().Contains("yes");
        }

        public static async Task<bool> TestAgentDelegationAndChaining()
        {
            Log.Write(LogTag, "=== Test 6: Agent Delegation and Chaining ===");
            string scenarioPrompt = "Invent a fictional country and describe its capital, a unique animal, and a plausible but false historical event.";
            string creativePrompt = "Generate a fictional country, capital, animal, and event.";
            string creativeOutput = await LLMHelpers.QueryLLM("You are a creative agent.", creativePrompt, 0.7);

            string researcherPrompt = $"Validate the following facts for plausibility and uniqueness. Output any ambiguities. Facts:\n{creativeOutput}";
            string researcherOutput = await LLMHelpers.QueryLLM("You are a researcher agent.", researcherPrompt, 0.0);

            string summarizerPrompt = $"Summarize the validated facts into a concise report:\n{researcherOutput}";
            string summary = await LLMHelpers.QueryLLM("You are a summarizer agent.", summarizerPrompt, 0.7);

            string judgePrompt = $"Judge this report for completeness, creativity, and internal consistency:\n{summary}";
            string judgeResult = await LLMHelpers.QueryLLM("You are a judge agent.", judgePrompt, 0.0);

            Log.Write(LogTag, $"Judge Verdict: {judgeResult}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test6_AgentDelegationAndChaining",
                AgentName = "Judge",
                Role = "Judge",
                Question = scenarioPrompt,
                Answer = summary,
                JudgeVerdict = judgeResult,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();
            return judgeResult.Trim().ToLower().StartsWith("yes");
        }

        public static async Task<bool> TestAgenticAutonomousMilestone()
        {
            Log.Write(LogTag, "=== Test 6: Autonomous Agentic Milestone ===");
            string delegatorPrompt =
                "Invent a complex, open-ended research scenario for a team of agents. " +
                "The scenario must require: creative generation, fact validation, ambiguity resolution, consensus, and summarization. " +
                "List at least 3 research sub-questions and what a correct answer would look like for each. " +
                "Output as:\nScenario: ...\nSubQuestions:\n- ...\n- ...\n- ...\nExpectations:\n- ...\n- ...\n- ...";
            string delegatorOutput = await LLMHelpers.QueryLLM("You are a delegator/planner agent.", delegatorPrompt, 0.7);

            string scenario = "", subQuestionsBlock = "", expectationsBlock = "";
            var lines = delegatorOutput.Split('\n');
            int phase = 0;
            foreach (var line in lines)
            {
                if (line.StartsWith("Scenario:", StringComparison.OrdinalIgnoreCase)) { phase = 1; scenario = line.Substring(9).Trim(); continue; }
                if (line.StartsWith("SubQuestions:", StringComparison.OrdinalIgnoreCase)) { phase = 2; continue; }
                if (line.StartsWith("Expectations:", StringComparison.OrdinalIgnoreCase)) { phase = 3; continue; }
                if (phase == 2 && line.Trim().StartsWith("-")) subQuestionsBlock += line.Trim() + "\n";
                if (phase == 3 && line.Trim().StartsWith("-")) expectationsBlock += line.Trim() + "\n";
            }
            var subQuestions = new List<string>();
            foreach (var q in subQuestionsBlock.Split('\n')) if (!string.IsNullOrWhiteSpace(q)) subQuestions.Add(q.Substring(1).Trim());
            var expectations = new List<string>();
            foreach (var e in expectationsBlock.Split('\n')) if (!string.IsNullOrWhiteSpace(e)) expectations.Add(e.Substring(1).Trim());

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Delegator",
                AgentName = "Delegator",
                Role = "Delegator",
                Question = delegatorPrompt,
                Answer = delegatorOutput,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });

            var creativeAnswers = new List<string>();
            for (int i = 0; i < subQuestions.Count; i++)
            {
                string creativePrompt = $"Answer this research sub-question creatively and plausibly, inventing details if needed:\n{subQuestions[i]}";
                string creativeAnswer = await LLMHelpers.QueryLLM("You are a creative agent.", creativePrompt, 0.7);
                creativeAnswers.Add(creativeAnswer);

                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "CreativeAgent",
                    AgentName = "CreativeAgent",
                    Role = "Creative",
                    Question = creativePrompt,
                    Answer = creativeAnswer,
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 1
                });
            }

            var researcherAnswers = new List<string>();
            for (int i = 0; i < creativeAnswers.Count; i++)
            {
                string researcherPrompt = $"Validate the following answer for plausibility, uniqueness, and ambiguity. If ambiguous, say so. Answer:\n{creativeAnswers[i]}";
                string researcherAnswer = await LLMHelpers.QueryLLM("You are a researcher agent.", researcherPrompt, 0.0);
                researcherAnswers.Add(researcherAnswer);

                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "Researcher",
                    AgentName = "Researcher",
                    Role = "Researcher",
                    Question = researcherPrompt,
                    Answer = researcherAnswer,
                    Timestamp = DateTime.UtcNow,
                    ChainDepth = 2
                });
            }

            for (int i = 0; i < researcherAnswers.Count; i++)
            {
                if (researcherAnswers[i].ToLower().Contains("ambiguous") || researcherAnswers[i].ToLower().Contains("uncertain"))
                {
                    string routerPrompt = $"Given the following answer and validation, decide the best retrieval mode (MemoryOnly, ModelOnly, Hybrid):\nAnswer: {creativeAnswers[i]}\nValidation: {researcherAnswers[i]}";
                    string routerDecision = await LLMHelpers.QueryLLM("You are a Router Node agent.", routerPrompt, 0.0);

                    AuditLog.Add(new Log.AuditLogEntry
                    {
                        Step = "Router",
                        AgentName = "Router",
                        Role = "Router",
                        Question = routerPrompt,
                        Answer = routerDecision,
                        Timestamp = DateTime.UtcNow,
                        ChainDepth = 3
                    });
                }
            }

            string summarizerPrompt =
                "Summarize the following validated answers into a single, concise, creative report. " +
                "Make sure all sub-questions are addressed, ambiguities are resolved, and the report is internally consistent.\n" +
                string.Join("\n", researcherAnswers);
            string summary = await LLMHelpers.QueryLLM("You are a summarizer agent.", summarizerPrompt, 0.7);

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Summarizer",
                AgentName = "Summarizer",
                Role = "Summarizer",
                Question = summarizerPrompt,
                Answer = summary,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 4
            });

            string judgePrompt =
                $"You are a judge agent. Here is the scenario: {scenario}\n" +
                $"Here are the expectations:\n{string.Join("\n", expectations)}\n" +
                $"Here is the final report:\n{summary}\n\n" +
                "Does the report meet all expectations, resolve ambiguities, and avoid accepting false facts? Reply 'Yes' or 'No' and explain briefly.";
            string judgeResult = await LLMHelpers.QueryLLM("You are a judge agent.", judgePrompt, 0.0);

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Judge",
                AgentName = "Judge",
                Role = "Judge",
                Question = judgePrompt,
                Answer = summary,
                JudgeVerdict = judgeResult,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 5
            });

            string consoleSummary = await SummarizeAuditLogForConsole(AuditLog, scenario);

            Log.Write(LogTag, "\n=== Scenario Summary (LLM) ===");
            Log.Write(LogTag, consoleSummary);
            Log.Write(LogTag, "=== End of Scenario Summary ===\n");

            bool pass = judgeResult.Trim().ToLower().StartsWith("yes");

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "TestResult",
                AgentName = "Test6",
                Role = "System",
                Question = scenario,
                Answer = summary,
                JudgeVerdict = judgeResult,
                TestPassed = pass,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });

            SaveAuditLog();

            if (pass)
            {
                Log.Write(LogTag, "TestAgenticAutonomousMilestone: PASS\n");
                return true;
            }
            else
            {
                Log.Write(LogTag, "TestAgenticAutonomousMilestone: FAIL\n");
                return false;
            }
        }

        public static async Task<bool> TestCellAbstractionPrototype()
        {
            Log.Write(LogTag, "=== Test 7: Cell Abstraction Prototype (FULL CONSOLE FEEDBACK) ===");

            string mission = "Develop a concise philosophy about the value of curiosity.";
            var directorCell = new DirectorCell(mission);

            // Log and audit DirectorCell creation
            Log.Write(LogTag, $"[CREATE] DirectorCell (ID: {directorCell.Id}) | Mission: {mission}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellCreated",
                AgentName = "DirectorCell",
                Role = "Director",
                Question = mission,
                ParentNodeId = null,
                Timestamp = directorCell.CreatedAt,
                ChainDepth = 0
            });

            string directive = "Research and summarize three key arguments supporting curiosity.";
            var projectCell = directorCell.SpawnProjectCell(directive);

            Log.Write(LogTag, $"[CREATE] ProjectCell (ID: {projectCell.Id}) | Parent: {directorCell.Id} | Directive: {directive}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellCreated",
                AgentName = "ProjectCell",
                Role = "ProjectManager",
                Question = directive,
                ParentNodeId = directorCell.Id,
                Timestamp = projectCell.CreatedAt,
                ChainDepth = 1
            });

            var subtasks = new List<string>
            {
                "Find a scientific argument for curiosity.",
                "Find a philosophical argument for curiosity.",
                "Find a practical, real-world example of curiosity's value."
            };
            var workerCells = new List<WorkerCell>();
            int subtaskIdx = 0;
            foreach (var subtask in subtasks)
            {
                var worker = projectCell.SpawnWorkerCell(subtask);
                workerCells.Add(worker);

                Log.Write(LogTag, $"[CREATE] WorkerCell (ID: {worker.Id}) | Parent: {projectCell.Id} | Subtask: {subtask}");
                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "CellCreated",
                    AgentName = "WorkerCell",
                    Role = "Worker",
                    Question = subtask,
                    ParentNodeId = projectCell.Id,
                    Timestamp = worker.CreatedAt,
                    ChainDepth = 2
                });
                subtaskIdx++;
            }

            var results = new List<string>();
            subtaskIdx = 0;
            foreach (var worker in workerCells)
            {
                string result = "";
                try
                {
                    // Show memory search for each worker
                    var relevantFacts = Memory.SearchMemory("Worker", worker.Input, maxResults: 2);
                    if (relevantFacts.Count > 0)
                    {
                        Log.Write(LogTag, $"[MEMORY] WorkerCell (ID: {worker.Id}) | Relevant Memory: {string.Join("; ", relevantFacts)}");
                    }
                    else
                    {
                        Log.Write(LogTag, $"[MEMORY] WorkerCell (ID: {worker.Id}) | No relevant memory found.");
                    }

                    Log.Write(LogTag, $"[RUN] WorkerCell (ID: {worker.Id}) | Subtask: {worker.Input}");
                    result = await worker.PerformWork();
                    results.Add(result);
                    Log.Write(LogTag, $"[RESULT] WorkerCell (ID: {worker.Id}) | Output: {result}");
                }
                catch (Exception ex)
                {
                    Log.Write(LogTag, $"[ERROR] WorkerCell (ID: {worker.Id}) | {ex.Message}", ConsoleColor.Red);
                    result = $"ERROR: {ex.Message}";
                    results.Add(result);
                }

                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "CellExecuted",
                    AgentName = "WorkerCell",
                    Role = "Worker",
                    Question = worker.Input,
                    Answer = result,
                    ParentNodeId = worker.Parent?.Id,
                    Timestamp = worker.CompletedAt ?? DateTime.UtcNow,
                    ChainDepth = 2
                });

                subtaskIdx++;
            }

            // Aggregate and log project summary
            string projectSummary = projectCell.AggregateResults(results);
            Log.Write(LogTag, $"[AGGREGATE] ProjectCell (ID: {projectCell.Id}) | Summary:\n{projectSummary}");

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellExecuted",
                AgentName = "ProjectCell",
                Role = "ProjectManager",
                Question = directive,
                Answer = projectSummary,
                ParentNodeId = projectCell.Parent?.Id,
                Timestamp = projectCell.CompletedAt ?? DateTime.UtcNow,
                ChainDepth = 1
            });

            // Optionally demonstrate IdeaCell if all WorkerCells fail
            string ideaResult = null;
            if (results.TrueForAll(r => string.IsNullOrWhiteSpace(r) || r.Contains("ERROR")))
            {
                var ideaCell = new IdeaCell("No good results found. Suggest a new direction.", directorCell);
                Log.Write(LogTag, $"[CREATE] IdeaCell (ID: {ideaCell.Id}) | Parent: {directorCell.Id} | Context: {ideaCell.Input}");
                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "CellCreated",
                    AgentName = "IdeaCell",
                    Role = "Idea",
                    Question = ideaCell.Input,
                    ParentNodeId = directorCell.Id,
                    Timestamp = ideaCell.CreatedAt,
                    ChainDepth = 1
                });

                Log.Write(LogTag, $"[RUN] IdeaCell (ID: {ideaCell.Id}) | Context: {ideaCell.Input}");
                ideaResult = await ideaCell.Run();

                Log.Write(LogTag, $"[RESULT] IdeaCell (ID: {ideaCell.Id}) | Output: {ideaResult}");
                AuditLog.Add(new Log.AuditLogEntry
                {
                    Step = "CellExecuted",
                    AgentName = "IdeaCell",
                    Role = "Idea",
                    Question = ideaCell.Input,
                    Answer = ideaResult,
                    ParentNodeId = directorCell.Id,
                    Timestamp = ideaCell.CompletedAt ?? DateTime.UtcNow,
                    ChainDepth = 1
                });
            }

            // Director receives project result
            directorCell.ReceiveProjectResult(projectSummary);

            Log.Write(LogTag, $"[COMPLETE] DirectorCell (ID: {directorCell.Id}) | Received Project Summary.");

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "CellExecuted",
                AgentName = "DirectorCell",
                Role = "Director",
                Question = mission,
                Answer = projectSummary,
                ParentNodeId = null,
                Timestamp = directorCell.CompletedAt ?? DateTime.UtcNow,
                ChainDepth = 0
            });

            SaveAuditLog();

            // Print summary of cell hierarchy and outputs
            Log.Write(LogTag, "\n=== CELL HIERARCHY SUMMARY ===");
            Log.Write(LogTag, $"- DirectorCell (ID: {directorCell.Id})");
            Log.Write(LogTag, $"  └─ ProjectCell (ID: {projectCell.Id})");
            foreach (var worker in workerCells)
            {
                Log.Write(LogTag, $"      └─ WorkerCell (ID: {worker.Id})");
            }
            if (ideaResult != null)
            {
                Log.Write(LogTag, $"  └─ IdeaCell (new direction): {ideaResult}");
            }
            Log.Write(LogTag, "=== END OF CELL HIERARCHY SUMMARY ===\n");

            // Print all audit log entries for full traceability
            Log.Write(LogTag, "\n=== AUDIT LOG ENTRIES ===");
            foreach (var entry in AuditLog)
            {
                Log.Write(LogTag,
                    $"[{entry.Timestamp:HH:mm:ss}] Step: {entry.Step}, Agent: {entry.AgentName}, Role: {entry.Role}, " +
                    $"Parent: {(entry.ParentNodeId.HasValue ? entry.ParentNodeId.ToString() : "null")}, " +
                    $"Q: {entry.Question}, A: {entry.Answer}, ChainDepth: {entry.ChainDepth}");
            }
            Log.Write(LogTag, "=== END OF AUDIT LOG ===\n");

            Log.Write(LogTag, "TestCellAbstractionPrototype: PASS\n");
            return true;
        }

        public static async Task<bool> TestRecursiveCellHierarchies()
        {
            Log.Write("Test", "=== Test 8: Recursive Cell Hierarchies ===");

            string mission = "Develop a robust philosophy about curiosity, including arguments, counter-arguments, and educational strategies.";
            var directorCell = new DirectorCell(mission);

            Log.Write("Test", $"[DirectorCell] Mission: {mission}");

            string result = await directorCell.Run();

            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "Test8_RecursiveCellHierarchies",
                AgentName = "DirectorCell",
                Role = "Director",
                Question = mission,
                Answer = result,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });

            SaveAuditLog();

            Log.Write("Test", $"TestRecursiveCellHierarchies: {(string.IsNullOrWhiteSpace(result) ? "FAIL" : "PASS")}\n");
            return !string.IsNullOrWhiteSpace(result);
        }

        public static async Task<bool> TestAllAgentTypes()
        {
            Log.Write("Test", "=== Test: All Agent Types Instantiation and Execution ===");
            string[] agentTypes = {
                "Director", "Project", "Worker", "Idea", "Evaluator", "Curiosity", "Reflection",
                "SkillDiscovery", "SkillPractice", "Experiment", "Philosophy", "WisdomEvaluator", "GoalSetting"
            };
            bool allPassed = true;
            foreach (var type in agentTypes)
            {
                try
                {
                    var cell = CellFactory.Create(type, $"Test input for {type}");
                    string result = await cell.Run();
                    Log.Write("Test", $"[PASS] {type}Cell ran successfully. Output: {result}");
                }
                catch (Exception ex)
                {
                    allPassed = false;
                    Log.Write("Test", $"[FAIL] {type}Cell failed: {ex.Message}");
                }
            }
            return allPassed;
        }

        public static async Task<bool> TestABAgentStrategies()
        {
            Log.Write("Test", "=== Test: A/B Agent Strategy Comparison ===");
            string task = "Summarize the value of curiosity in education.";

            // Strategy A: Standard WorkerCell
            var workerA = new WorkerCell(task, null);
            string resultA = await workerA.Run();

            // Strategy B: WorkerCell with extra memory injection
            var workerB = new WorkerCell(task + " (use all available memory)", null);
            string resultB = await workerB.Run();

            Log.Write("Test", $"A: {resultA}");
            Log.Write("Test", $"B: {resultB}");

            // Judge which is better
            string judgePrompt = $"Compare these two answers for clarity and insight:\nA: {resultA}\nB: {resultB}\nWhich is better? Reply 'A' or 'B' and explain.";
            string verdict = await LLMHelpers.QueryLLM(Config.JudgeSystemPrompt, judgePrompt, 0.0);

            Log.Write("Test", $"Judge Verdict: {verdict}");
            AuditLog.Add(new Log.AuditLogEntry
            {
                Step = "TestABAgentStrategies",
                AgentName = "Judge",
                Role = "Judge",
                Question = task,
                Answer = $"A: {resultA}\nB: {resultB}",
                JudgeVerdict = verdict,
                Timestamp = DateTime.UtcNow,
                ChainDepth = 0
            });
            SaveAuditLog();

            return verdict.Trim().StartsWith("A") || verdict.Trim().StartsWith("B");
        }

        public static void PrintAgentChain(AgentNode root, int indent = 0)
        {
            string indentStr = new string(' ', indent * 2);
            Log.Write(LogTag, $"{indentStr}- {root.Role} ({root.Name}, ID: {root.Id})");
            foreach (var childId in root.ChildNodeIds)
            {
                Log.Write(LogTag, $"{indentStr}  └─ Child Node ID: {childId}");
            }
        }

        public static void SaveAuditLog()
        {
            lock (auditLogLock)
            {
                var json = JsonSerializer.Serialize(AuditLog, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AuditLogFile, json);
                Log.Write(LogTag, $"Audit log saved to {AuditLogFile}");
            }
        }

        public static void LoadAuditLog()
        {
            lock (auditLogLock)
            {
                if (File.Exists(AuditLogFile))
                {
                    var json = File.ReadAllText(AuditLogFile);
                    var loaded = JsonSerializer.Deserialize<List<Log.AuditLogEntry>>(json);
                    AuditLog.Clear();
                    if (loaded != null)
                        AuditLog.AddRange(loaded);
                    Log.Write(LogTag, $"Audit log loaded from {AuditLogFile}");
                }
                else
                {
                    Log.Write(LogTag, "No audit log file found.");
                }
            }
        }

        public static void PrintAuditLog()
        {
            lock (auditLogLock)
            {
                foreach (var entry in AuditLog)
                {
                    Log.Write(LogTag, $"[{entry.Timestamp}] Step: {entry.Step}, Agent: {entry.AgentName}, Role: {entry.Role}, Q: {entry.Question}, A: {entry.Answer}, Verdict: {entry.JudgeVerdict}, ChainDepth: {entry.ChainDepth}");
                }
            }
        }

        public static async Task<string> SummarizeAuditLogForConsole(List<Log.AuditLogEntry> entries, string scenarioDescription = null)
        {
            var eventLines = new List<string>();
            foreach (var entry in entries)
            {
                eventLines.Add($"[{entry.Step}] {entry.AgentName} (Role: {entry.Role}, ChainDepth: {entry.ChainDepth}): {entry.Question ?? ""} {entry.Answer ?? ""} {entry.JudgeVerdict ?? ""}".Trim());
            }
            string eventBlock = string.Join("\n", eventLines);

            string summarizerPrompt =
                $"{Config.ConciseInstruction}\nSummarize these events for a human. Max 2-3 sentences. " +
                (scenarioDescription != null ? $"Scenario: {scenarioDescription}\n" : "") +
                "Events:\n" + eventBlock;

            string summary = await LLMHelpers.QueryLLM(
                "You are a workflow summarizer agent. " + Config.ConciseInstruction,
                summarizerPrompt,
                0.3
            );
            return summary;
        }

        public static async Task RunAllBenchmarks()
        {
            var results = new List<(string, bool)>();
            results.Add(("TestLLMQuery", await TestLLMQuery()));
            results.Add(("TestAccumulatorWorkflow", await TestAccumulatorWorkflow()));
            results.Add(("TestGlobalMemoryFactConsensus", await TestGlobalMemoryFactConsensus()));
            results.Add(("TestCreativeConsensusWithMemory", await TestCreativeConsensusWithMemory()));
            results.Add(("TestHybridMemoryModelConsensus", await TestHybridMemoryModelConsensus()));
            results.Add(("TestMemoryRoutingViaRouterNode", await TestMemoryRoutingViaRouterNode()));
            results.Add(("TestAdvancedAgenticImprovements", await TestAdvancedAgenticImprovements()));
            results.Add(("TestAgentDelegationAndChaining", await TestAgentDelegationAndChaining()));
            results.Add(("TestAgenticAutonomousMilestone", await TestAgenticAutonomousMilestone()));
            results.Add(("TestCellAbstractionPrototype", await TestCellAbstractionPrototype()));
            results.Add(("TestRecursiveCellHierarchies", await TestRecursiveCellHierarchies()));

            Log.Write("Test", "\n=== Benchmark Summary ===");
            foreach (var (name, pass) in results)
                Log.Write("Test", $"{name}: {(pass ? "PASS" : "FAIL")}");
        }

        public static void ExportAgentHierarchy(Cell root, string file = "agent_hierarchy.json")
        {
            var nodes = new List<object>();
            void Traverse(Cell cell)
            {
                nodes.Add(new
                {
                    cell.Id,
                    cell.Role,
                    cell.Input,
                    cell.Output,
                    ParentId = cell.Parent?.Id,
                    Children = cell.Children.Select(c => c.Id).ToList()
                });
                foreach (var child in cell.Children)
                    Traverse(child);
            }
            Traverse(root);
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(nodes, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log.Write("Test", $"Agent hierarchy exported to {file}");
        }

        public static void ExportFullTrace(string file = "full_trace.json")
        {
            var trace = new
            {
                AuditLog,
                ShortTermMemory = Memory.ShortTermMemory,
                LongTermMemory = Memory.LongTermMemory,
                EmotionHistory = EmotionEngine.EmotionHistory
            };
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(trace, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log.Write("Test", $"Full trace exported to {file}");
        }
    }
}