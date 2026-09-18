using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketTriage;

/// <summary>
/// The body of POST /api/triage: the selected ticket ID, an optional question and an optional temporary
/// scenario that supplies missing intake facts. It is validated before any model call.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TriageRequest(string? TicketId, string? Question = null,
    Dictionary<string, JsonElement>? Scenario = null)
{
    public (string Id, string? Question, TicketStore Store) Validate(TicketStore store)
    {
        var id = TicketId?.Trim();
        if (!TicketStore.ValidId(id)) throw new ArgumentException("valid_ticket_id_required");
        var question = Question?.Trim();
        if (question?.Length > 1_000) throw new ArgumentException("question_too_long");
        // A scenario yields a request-local store (TicketStore.WithScenario) and never edits the fixture.
        return (id!, string.IsNullOrEmpty(question) ? null : question, store.WithScenario(id!, Scenario));
    }
}

public sealed record ScenarioInfo(bool Active, IReadOnlyList<Evidence> SuppliedFields);
