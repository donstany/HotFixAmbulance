namespace HotFixAmbulance.Llm;

/// <summary>
/// A system + user message pair to send to a chat-style LLM. Built by <see cref="LlmPromptBuilder"/>
/// from an <see cref="HotFixAmbulance.Core.ErrorGroup"/> and the git-history evidence for that group.
/// </summary>
public sealed record LlmPrompt(string System, string User);
