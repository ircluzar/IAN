using System;
using System.Collections.Generic;

namespace IAN_Core
{
    // Base abstract class for agent context nodes
    public abstract class NodeEntity
    {
        public Guid Id { get; set; } // Unique identifier for the node
        public string Name { get; set; } // Human-readable name for debugging/logging
        public string Type { get; set; } // Type/category of the node (for context differentiation)
        public DateTime CreatedAt { get; set; } // Timestamp for creation (for auditing/history)
        public DateTime UpdatedAt { get; set; } // Timestamp for last update (for synchronization)
        public bool IsActive { get; set; } // Indicates if the node is currently active (for state management)
        public string Description { get; set; } // Optional description (for documentation/context)
    }

    // 1. Agent that verifies or queries info against a knowledge base
    public class KnowledgeVerifierNode : NodeEntity
    {
        public string KnowledgeBaseSource { get; set; } // e.g., database name or API endpoint
        public string Query { get; set; } // The query or verification statement
        public bool VerificationResult { get; set; } // Result of the verification
    }

    // 2. Agent that spawns more agents for consensus and judges the consensus
    public class ConsensusOrchestratorNode : NodeEntity
    {
        public List<Guid> SpawnedAgentIds { get; set; } = new List<Guid>(); // IDs of spawned agents
        public string Question { get; set; } // The question for consensus
        public string ConsensusResult { get; set; } // Final consensus answer
        public bool IsConsensusReached { get; set; } // Indicates if consensus was achieved
    }

    // 3. Agent that answers a question as part of a consensus
    public class ConsensusResponderNode : NodeEntity
    {
        public Guid OrchestratorId { get; set; } // The orchestrator agent's ID
        public string ReceivedQuestion { get; set; } // The question to answer
        public string Response { get; set; } // The agent's answer
        public bool WasSelectedForConsensus { get; set; } // If this response was chosen
        public AgentRetrievalMode RetrievalMode { get; set; } // Mode of agent retrieval
    }

    // Represents a node with access to the LLM (last mile node)
    public class LastMileNode : NodeEntity
    {
        public string LLMEndpoint { get; set; } // Endpoint or identifier for the LLM
        public string LastResponse { get; set; } // Stores the last response from the LLM
    }

    /// <summary>
    /// Generic agent node for delegation/chaining scenarios.
    /// </summary>
    public class AgentNode : NodeEntity
    {
        /// <summary>
        /// The explicit agent role (e.g., "Delegator", "Researcher", "Summarizer", "Judge").
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The parent node that delegated to this agent (null for root).
        /// </summary>
        public Guid? ParentNodeId { get; set; }

        /// <summary>
        /// All child nodes spawned by this agent.
        /// </summary>
        public List<Guid> ChildNodeIds { get; set; } = new List<Guid>();

        /// <summary>
        /// The depth of this node in the delegation chain (root = 0).
        /// </summary>
        public int ChainDepth { get; set; }
    }

    // Handles transactions between nodes and the last mile node
    public class Responder
    {
        public NodeEntity SourceNode { get; set; }
        public LastMileNode LastMile { get; set; }

        public Responder(NodeEntity sourceNode, LastMileNode lastMile)
        {
            SourceNode = sourceNode;
            LastMile = lastMile;
        }

        // Sends a question to the last mile node and returns the response
        public string SendToLastMile(string question)
        {
            // Simulate sending to LLM (replace with actual LLM call)
            LastMile.LastResponse = $"[LLM Response to: {question}]";
            return LastMile.LastResponse;
        }

        // Example: relay a node's question to the LLM and store the answer
        public void RelayNodeQuestion()
        {
            if (SourceNode is ConsensusResponderNode responderNode)
            {
                string answer = SendToLastMile(responderNode.ReceivedQuestion);
                responderNode.Response = answer;
            }
        }
    }
}

