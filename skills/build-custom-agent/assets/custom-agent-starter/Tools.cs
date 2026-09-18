using System.ComponentModel;

namespace CustomAgent;

/// <summary>
/// Tools the coding agent can add, change, or remove to fit the approved scenario.
/// Register one to three in AgentDefinition.CreateTools for the running model to call.
/// Each tool computes locally, reads declared content through KnowledgeBase, or reads an approved host through the
/// ApprovedHttpClient from CreateTools (Capabilities:Network:AllowedHosts in appsettings.json); none has side effects.
/// </summary>
public class AssistantTools(KnowledgeBase knowledgeBase)
{
    [Description("Search bundled Markdown, text, JSON, and CSV prototype content. Results identify the source and whether it is mock or supplied.")]
    public string SearchLocalContent(
        [Description("The question or keywords to find in local prototype content.")] string query)
        => knowledgeBase.Search(query);

    [Description("Read one known local prototype source using the exact source label returned by search.")]
    public string ReadLocalSource(
        [Description("The source label returned by SearchLocalContent.")] string source)
        => knowledgeBase.Read(source);

    [Description("Look up a record only in bundled JSON or CSV data. Results identify whether the source is mock or supplied; this never contacts or updates a live system.")]
    public string LookupLocalRecord(
        [Description("A record identifier or keywords to find in bundled structured data.")] string query)
        => knowledgeBase.SearchData(query);
}
