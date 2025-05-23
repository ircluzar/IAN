# IAM Agentic System Architecture

---

## Table of Contents

1. [Overview](#overview)
2. [Core Concepts](#core-concepts)
    - [Cell Abstraction](#cell-abstraction)
    - [Agentic Hierarchy](#agentic-hierarchy)
    - [Memory System](#memory-system)
    - [Consensus & Delegation](#consensus--delegation)
    - [Audit Logging & Traceability](#audit-logging--traceability)
3. [Modules & Components](#modules--components)
    - [Config](#config)
    - [Log](#log)
    - [Memory](#memory)
    - [Nodes](#nodes)
    - [Accumulator](#accumulator)
    - [Intel & LLMHelpers](#intel--llmhelpers)
    - [Testing](#testing)
    - [Program](#program)
4. [Agentic Workflows](#agentic-workflows)
    - [Director Workflow](#director-workflow)
    - [Project Workflow](#project-workflow)
    - [Worker Workflow](#worker-workflow)
    - [Idea & Evaluator Workflows](#idea--evaluator-workflows)
    - [Consensus Workflow](#consensus-workflow)
5. [Memory Management](#memory-management)
    - [Short-Term Memory](#short-term-memory)
    - [Long-Term Memory](#long-term-memory)
    - [Memory Compression & Deduplication](#memory-compression--deduplication)
    - [Memory Relevance & Retrieval](#memory-relevance--retrieval)
6. [Mission Evolution & Reflection](#mission-evolution--reflection)
    - [Mission Growth](#mission-growth)
    - [Reflection Concert](#reflection-concert)
    - [Rollback & Diff](#rollback--diff)
7. [Audit & Testing](#audit--testing)
    - [Audit Log Structure](#audit-log-structure)
    - [Testing Scenarios](#testing-scenarios)
8. [Extensibility & Guidelines](#extensibility--guidelines)
    - [Adding New Agent Types](#adding-new-agent-types)
    - [Best Practices](#best-practices)
    - [Improvement Opportunities](#improvement-opportunities)
9. [Example Use Case Scenario: Building a Philosophy of Curiosity](#example-use-case-scenario-building-a-philosophy-of-curiosity)

---

## 1. Overview

IAM (IntelAutoMemory) is a modular, recursive, and auditable agentic system designed to orchestrate, test, and evolve multi-agent workflows using LLMs. It supports advanced memory management, consensus, mission evolution, and traceability, enabling autonomous and explainable agentic reasoning.

IAM is built to simulate and automate complex, multi-step reasoning and planning tasks. It does so by recursively spawning and coordinating specialized agents (Cells), each with its own role, memory, and workflow. The system is designed for transparency, extensibility, and robust experimentation with agentic architectures.

---

## 2. Core Concepts

### Cell Abstraction

- **Cell**: The fundamental unit of computation and workflow orchestration.
    - Each cell encapsulates a role (Director, Project, Worker, Idea, Evaluator, etc.), input, output, parent, children, and lifecycle timestamps.
    - Cells can spawn sub-cells, forming a recursive agentic hierarchy.
    - Cells are responsible for their own execution (`Run()`), memory interaction, and child management.
    - Cells can be composed to form arbitrarily deep and complex agentic workflows, supporting both breadth (parallelism) and depth (recursion).

### Agentic Hierarchy

- **Director Cell**: Holds the mission, orchestrates projects, and manages mission evolution. It is the root of the agentic tree and is responsible for high-level planning, delegation, and mission growth.
- **Project Cell**: Manages a directive, spawns WorkerCells, aggregates results, and reports back to the Director. It acts as a project manager, breaking down tasks and ensuring completion.
- **Worker Cell**: Executes a specific subtask, can spawn sub-workers for further decomposition. WorkerCells are the main executors of atomic tasks.
- **Idea Cell**: Generates new ideas or directions when the Director needs inspiration or is out of directives. It enables creative expansion and prevents stagnation.
- **Evaluator Cell**: Judges the quality or correctness of outputs, providing feedback and validation for other cells.
- **Consensus Orchestrator/Responder Nodes**: Used for multi-agent consensus workflows, where multiple agents propose answers and a consensus is determined.

### Memory System

- **Short-Term Memory**: Fast, high-capacity, volatile; stores recent facts and outputs per node type. Used for immediate context and recent results.
- **Long-Term Memory**: Compressed, persistent, lower-capacity; stores important, deduplicated, and summarized knowledge. Used for knowledge retention and transfer across sessions.
- **Memory Compression**: Uses LLMs to merge, summarize, and deduplicate facts for long-term storage. Prevents memory bloat and ensures relevance.
- **Memory Relevance**: LLM-based scoring to select the most relevant facts for context windows. Ensures that only the most pertinent information is injected into agent prompts.

### Consensus & Delegation

- **Consensus**: Multiple sub-agents answer a question; an orchestrator uses LLMs to determine the consensus answer. Supports various consensus types (word, number, bool, knowledge).
- **Delegation**: Cells can spawn sub-cells or nodes to handle subtasks, forming a chain of responsibility. This enables recursive task decomposition and parallel execution.

### Audit Logging & Traceability

- **Audit Log**: Every agent action, memory change, and mission update is logged with full context, timestamps, and relationships. Enables full replay and analysis of system behavior.
- **Traceability**: All workflows, memory changes, and decisions are auditable and can be replayed or summarized. Supports debugging, transparency, and research.

---

## 3. Modules & Components

### Config

- Centralizes all configuration: LLM endpoints, model names, sub-agent count, prompt templates, enums, and consensus types.
- Stores agent role prompts and retrieval strategies, making it easy to adjust system behavior and add new agent types.
- Handles mission persistence and loading.

### Log

- Centralized, colorized logging utility for all system output.
- Defines `AuditLogEntry` for structured audit logging, including step, agent, role, parent/child, verdict, timestamps, and more.
- Supports tag-based coloring and long-text highlighting for improved readability.

### Memory

- Manages unified short-term and long-term memory pools (per node type and global).
- Handles loading, saving, compression, deduplication, and relevance scoring.
- Provides methods for adding, retrieving, compacting, and cleaning memory.
- Implements memory compression with LLMs, mapping original facts to compressed blobs for traceability.
- Supports memory search and relevance scoring for optimal context window usage.

### Nodes

- Defines all agent node types:
    - `NodeEntity` (base): Common metadata for all nodes.
    - `ConsensusOrchestratorNode`, `ConsensusResponderNode`: For consensus workflows.
    - `KnowledgeVerifierNode`, `LastMileNode`, `AgentNode`: For specialized agentic roles and delegation.
- Tracks metadata: type, role, parent/child, retrieval mode, timestamps, and more.

### Accumulator

- Orchestrates consensus workflows, spawning sub-agents and collecting their answers.
- Supports advanced memory injection (short-term, long-term, hybrid) and prompt diversity for sub-agents.
- Determines consensus using LLMs and logs all steps for traceability.

### Intel & LLMHelpers

- **Intel**: Handles all LLM API calls (OpenAI-compatible chat completions), including message formatting and HTTP requests.
- **LLMHelpers**: Provides helper methods for querying the LLM, generating facts/questions/answers, extracting answers, and handling retries/timeouts.

### Testing

- Contains all system tests, including:
    - LLM Query, Accumulator Workflow, Global Memory Fact Consensus, Creative Consensus, Hybrid Reasoning, Memory Routing, Advanced Agentic Improvements, Autonomous Milestone, Cell Abstraction, Recursive Hierarchies.
- Implements LLM-based summarization of audit logs for readable console output.
- Supports memory wiping, loading, and saving for reproducible tests.

### Program

- Entry point for running tests and managing memory lifecycle.
- Supports toggling test groups and wipes/loads/saves memory at program start/exit.
- Can run the full Machine workflow or individual tests.

---

## 4. Agentic Workflows

### Director Workflow

- Receives the mission and seeds initial directives (ideas or projects).
- Spawns ProjectCells for each directive, managing parallel or sequential execution.
- If out of ideas, spawns an IdeaCell for new directions, ensuring continuous progress.
- Aggregates results from ProjectCells and marks completion when all projects/ideas are done.
- Responsible for mission evolution and high-level orchestration.

### Project Workflow

- Receives a directive from the Director and breaks it down into subtasks.
- Spawns WorkerCells for each subtask, managing their execution and aggregation.
- Aggregates WorkerCell results and reports back to the Director.
- Can spawn EvaluatorCells for quality control or further ProjectCells for deeper decomposition.

### Worker Workflow

- Receives a subtask from a ProjectCell and executes it, possibly spawning sub-workers for complex tasks.
- Returns results to the ProjectCell, contributing to the overall project outcome.
- Can perform atomic work or recursively decompose tasks as needed.

### Idea & Evaluator Workflows

- **IdeaCell**: Generates new ideas or next steps using LLMs and current context, enabling creative expansion and preventing stagnation.
- **EvaluatorCell**: Judges outputs for correctness, quality, or relevance, providing feedback and validation for other cells. Can be used for automated grading, filtering, or consensus judging.

### Consensus Workflow

- Orchestrator node spawns multiple responder sub-agents, each answering the question independently (with or without memory).
- Orchestrator uses LLM to determine consensus from sub-agent answers, supporting various consensus types (word, number, bool, knowledge).
- Consensus result is stored and logged, and can be used for further reasoning or memory updates.
- Supports prompt diversity and memory injection for robust consensus.

---

## 5. Memory Management

### Short-Term Memory

- Stores recent facts, outputs, and context per node type.
- Volatile and high-capacity; compacted regularly to avoid bloat and ensure relevance.
- Used for immediate context in agent prompts and reasoning.

### Long-Term Memory

- Stores compressed, deduplicated, and summarized knowledge for each node type.
- Persistent and lower-capacity; optimized for relevance, traceability, and knowledge retention.
- Used for knowledge transfer across sessions and long-term reasoning.

### Memory Compression & Deduplication

- Uses LLMs to merge, summarize, and deduplicate facts before storing in long-term memory.
- Compressed blobs are flagged with `[COMPRESSED|timestamp]` and mapped to original facts for traceability.
- Prevents overcompression by skipping recently compressed blobs (based on timestamp).
- Deduplication is performed both before and after compression, using exact and fuzzy (LLM/Levenshtein) matching.

### Memory Relevance & Retrieval

- LLM-based scoring ranks facts for relevance to the current mission or query.
- Most relevant facts are injected into agent prompts for context-aware reasoning.
- Hybrid and memory-only agents use memory blocks in their prompts, ensuring that reasoning is grounded in both recent and persistent knowledge.
- Memory search prioritizes short-term memory, then long-term, for optimal context window usage.

---

## 6. Mission Evolution & Reflection

### Mission Growth

- The mission is not static; agents collaboratively expand and clarify it over time.
- Reflection agents propose improvements, clarifications, or expansions based on memory and recent results.
- Mission growth is tracked, logged, and can be rolled back if needed.

### Reflection Concert

- Multiple reflection agents receive diverse memory slices and propose mission changes.
- If a majority propose meaningful growth, the mission is updated.
- All proposals and rationales are logged for traceability, including which agents proposed the accepted mission.
- Reflection diversity ensures richer and less biased mission evolution.

### Rollback & Diff

- Mission history is tracked with timestamps, old/new values, and rationales.
- Rollback to previous missions is supported via CLI, enabling recovery from undesirable changes.
- Word-level diff highlights changes between old and new missions, aiding transparency and review.

---

## 7. Audit & Testing

### Audit Log Structure

- Every agent action, memory change, and mission update is logged as an `AuditLogEntry`.
- Entries include step, agent name, role, parent/child, retrieval mode, verdict, timestamps, and chain depth.
- Audit log can be saved, loaded, printed, and summarized for full traceability and debugging.

### Testing Scenarios

- Comprehensive tests cover:
    - LLM query and prompt handling
    - Consensus workflows (with and without memory)
    - Memory-only, model-only, and hybrid retrieval
    - Memory routing and advanced agentic improvements
    - Autonomous, multi-step, multi-agent scenarios
    - Cell abstraction and recursive hierarchies
- LLM-based summarizer condenses audit logs for human readability, supporting rapid review and debugging.
- Tests can be run individually or as part of the full Machine workflow.

---

## 8. Extensibility & Guidelines

### Adding New Agent Types

- Define a new Cell or Node class with appropriate role and workflow.
- Register prompts and retrieval strategies in `Config` for consistency.
- Ensure all actions are logged and auditable for traceability.
- Update documentation and tests to cover new agent types and workflows.

### Best Practices

- Use the `Config` class for all configuration and prompts to centralize changes.
- Use class-level `LogTag` for all logging to maintain clarity in output.
- Prefer modular, agentic, and auditable designs for maintainability and transparency.
- Keep logging verbose and colorized for traceability and debugging.
- Document all new modules, agent types, and workflows in this architecture file to keep the system understandable.

### Improvement Opportunities

- Dynamic agent role assignment and chaining for more flexible workflows.
- Smarter, semantic memory relevance and retrieval using advanced LLM techniques.
- UI/visualization for agent workflows and memory graphs to aid understanding and debugging.
- More robust error handling and recovery for failed LLM calls or agent steps.
- Expanded LLM-based summarization and feedback loops for continuous system improvement.

---

## 9. Example Use Case Scenario: Building a Philosophy of Curiosity

### Scenario Overview

Suppose the IAM system is given the high-level mission:  
**"Develop a robust philosophy about curiosity, including arguments, counter-arguments, and educational strategies."**

This scenario demonstrates how the system’s architecture, memory, and agentic workflows interact to autonomously accomplish a complex, multi-step reasoning and synthesis task.

---

### Step-by-Step Workflow

#### 1. **Mission Initialization**

- The **DirectorCell** is instantiated with the mission statement.
- The mission is logged, and the DirectorCell becomes the root of the agentic workflow tree.

#### 2. **Directive Generation**

- The DirectorCell seeds initial directives, such as:
    - "Research and summarize three key arguments supporting curiosity."
    - "Identify common counter-arguments to curiosity."
    - "Propose educational strategies to foster curiosity."
- Each directive is enqueued for execution.

#### 3. **Project Decomposition**

- For each directive, the DirectorCell spawns a **ProjectCell**.
    - Example: For "Research and summarize three key arguments supporting curiosity," a ProjectCell is created and attached as a child to the DirectorCell.
- Each ProjectCell logs its creation and directive.

#### 4. **Task Breakdown and Worker Spawning**

- Each ProjectCell decomposes its directive into subtasks.
    - Example subtasks for the first directive:
        - "Find a scientific argument for curiosity."
        - "Find a philosophical argument for curiosity."
        - "Find a practical, real-world example of curiosity's value."
- For each subtask, the ProjectCell spawns a **WorkerCell**.

#### 5. **Worker Execution and Memory Use**

- Each WorkerCell:
    - Retrieves relevant short-term and long-term memory (facts, prior arguments, examples).
    - Uses LLM-based relevance scoring to select the most pertinent facts.
    - Constructs a prompt including the subtask and relevant memory.
    - Queries the LLM for a response.
    - Logs the result and writes it to short-term memory.
    - If the subtask is complex, may recursively spawn sub-workers.

#### 6. **Aggregation and Evaluation**

- The ProjectCell aggregates results from its WorkerCells.
- If needed, it spawns an **EvaluatorCell** to judge the quality or correctness of the aggregated arguments.
    - The EvaluatorCell uses memory and LLM prompts to provide feedback or validation.
- The ProjectCell logs the aggregated summary and marks itself complete.

#### 7. **Director Aggregation and Idea Generation**

- The DirectorCell receives project summaries from all ProjectCells.
- If all directives are completed and no new ideas are pending, but the mission is not fully satisfied, the DirectorCell spawns an **IdeaCell**.
    - The IdeaCell reviews the mission and current accomplishments, then proposes new directions (e.g., "Explore the role of curiosity in scientific revolutions").
    - The DirectorCell enqueues these new directives for further project decomposition.

#### 8. **Consensus and Reflection**

- For ambiguous or open-ended questions (e.g., "What is the most important benefit of curiosity?"), the system uses the **Accumulator** to run a consensus workflow:
    - Spawns multiple sub-agents (ConsensusResponderNodes) with diverse memory/context.
    - Each sub-agent answers independently.
    - The ConsensusOrchestratorNode collects answers and uses the LLM to determine consensus.
    - The consensus result is logged and stored in memory.

- Periodically, a **Reflection Concert** is triggered:
    - Multiple reflection agents receive diverse memory slices and propose mission improvements.
    - If a majority propose meaningful growth, the mission is updated, logged, and a word-level diff is shown.
    - Mission history and rationales are recorded for traceability.

#### 9. **Memory Management Throughout**

- All facts, arguments, and results are written to short-term memory.
- When short-term memory exceeds thresholds, it is compacted and deduplicated.
- Excess or older facts are compressed (via LLM) and moved to long-term memory, with mappings for traceability.
- Memory is periodically cleaned and compressed to maintain relevance and avoid bloat.

#### 10. **Audit Logging and Traceability**

- Every cell creation, execution, memory change, consensus, and mission update is logged as an `AuditLogEntry`.
- The audit log can be printed, summarized, or replayed for full transparency.
- CLI tools allow inspection, rollback, and review of mission history and memory state.

---

### **End-to-End Example:**

1. **Mission:** "Develop a robust philosophy about curiosity, including arguments, counter-arguments, and educational strategies."
2. **DirectorCell** spawns ProjectCells for each directive.
3. **ProjectCells** spawn WorkerCells for subtasks.
4. **WorkerCells** use memory and LLMs to generate arguments/examples.
5. **ProjectCells** aggregate and, if needed, evaluate results.
6. **DirectorCell** collects all summaries; if gaps remain, spawns IdeaCell for new directions.
7. **Consensus workflows** resolve ambiguous questions.
8. **Reflection Concert** periodically improves the mission.
9. **Memory** is compressed, deduplicated, and managed throughout.
10. **Audit log** records every step for transparency and debugging.

---

### **Key Takeaways**

- The IAM system autonomously decomposes, executes, and refines complex reasoning tasks.
- Memory is leveraged at every step for context, learning, and traceability.
- The mission evolves over time, driven by agent reflection and consensus.
- Every action is auditable, and the system is robust to errors, ambiguity, and change.

---

**This scenario demonstrates how IAM’s architecture, workflows, and memory systems work together to accomplish sophisticated, multi-agent reasoning and synthesis tasks—fully autonomously and with complete transparency.**