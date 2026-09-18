using System.ComponentModel;

namespace OperationsDataAnalyst;

/// <summary>
/// The tools the model can call, registered in AgentRuntime. The model chooses a tool and its arguments from the
/// [Description] text. One instance and one tool call per question: ExploreMetrics calculates and updates the
/// dashboard, GetCurrentContext explains the current view, and ExplainLimitation covers unsupported requests.
/// </summary>
public class AssistantTools(MetricsStore metrics, ViewRules rules, ViewSpec current, bool fixedView,
    CancellationTokenSource budget, Func<ViewData, Task>? onView = null)
{
    private int _calls;
    public ViewData? Data { get; private set; }
    public ToolEvidence? Evidence { get; private set; }
    public LimitationResult? Limitation { get; private set; }

    [Description("Calculate supported synthetic metrics and update the dashboard: request/error totals, error rates, average response times, peaks/lows, period changes and outliers. Cannot calculate arbitrary statistics; use ExplainLimitation when the requested quantity is unavailable. Use GetCurrentContext for definitions, interpretation or data questions that need no new calculation. Service rankings use service bars; busiest days use requests by day. Null filters inherit the current view. Use service all only for a requested comparison across services. For period comparisons provide four ordered, nonoverlapping inclusive dates. Use returned chart points as authoritative values and peak/lowest for extremes including ties. Set includeOutliers for anomaly questions. Set includeAllMetrics only for an overview or explicitly requested multiple metrics. Call once; supported calculations are returned together.")]
    public async Task<FocusedExplorationResult> ExploreMetrics(string? service = null, string? start = null, string? end = null,
        string? metric = null, string? grouping = null, ComparisonWindows? comparison = null, bool includeOutliers = false, bool includeAllMetrics = false)
    {
        BeginCall();
        // A dashboard click fixes the view; otherwise the model's changes are merged into the current view.
        var view = fixedView ? current : rules.Merge(current, new(service, start, end, metric, grouping, comparison));
        var summary = metrics.Summarize(view.Service, view.Start, view.End);
        var periods = view.Comparison is { } windows
            ? metrics.Compare(view.Service, windows.BeforeStart, windows.BeforeEnd, windows.AfterStart, windows.AfterEnd) : null;
        var outliers = includeOutliers ? metrics.FindOutliers(view.Service, view.Start, view.End) : null;
        Data = rules.Data(view);
        var result = new ExplorationResult(view, Data.Chart, summary, periods, outliers);
        // Daily request peaks stay available only where they coincide with the plotted
        // series, so they can never contradict the generalized peak below.
        var dailyRequestPeak = view is { Metric: "requests", Grouping: "day" };
        var (peak, lowest) = ViewRules.Extremes(Data.Chart);
        var focused = new FocusedExplorationResult(view, Data.Chart, ViewRules.Value(summary.Summary, view.Metric),
            periods is null ? null : ViewRules.Value(periods.After, view.Metric) - ViewRules.Value(periods.Before, view.Metric),
            dailyRequestPeak ? summary.BusiestDays : null,
            dailyRequestPeak ? summary.MaxDailyRequests : null, peak, lowest, outliers,
            includeAllMetrics ? new(summary, periods) : null);
        Evidence = new(nameof(ExploreMetrics), new ExplorationArguments(view, includeOutliers, includeAllMetrics), result, focused);
        budget.Token.ThrowIfCancellationRequested();
        // Publish the chart as soon as the numbers exist, before the model writes the explanation.
        if (onView is not null) await onView(Data);
        return focused;
    }

    [Description("Read the current chart, metric tiles and data scope. Use for definitions, interpretation, why-questions, simpler explanations, provenance and limitations, even when the question mentions a metric. Does not calculate new statistics or change the dashboard. Use ExploreMetrics only for supported calculations or a different selection.")]
    public CurrentContextResult GetCurrentContext()
    {
        BeginCall();
        var data = rules.Data(current);
        var result = new CurrentContextResult(current, data.Chart, data.Highlights,
            "Synthetic daily totals and averages. No individual request durations or live data. " +
            "Percentile and median tiles describe the plotted aggregate values, not individual requests.");
        Evidence = new(nameof(GetCurrentContext), new { }, result, result);
        return result;
    }

    [Description("Use when the request needs clarification or asks for a calculation, data or action the tools cannot provide. Never substitute another metric for an unavailable statistic. status must be clarify or unsupported. Returns scope limitations for a concise explanation specific to the question. No dashboard change occurs.")]
    public LimitationResult ExplainLimitation(string status)
    {
        BeginCall();
        if (status is not ("clarify" or "unsupported")) throw new ArgumentException("unsupported_limitation_status");
        Limitation = new(status, status == "clarify"
            ? "Which service, dates or metric would you like to explore?"
            : "The bundled synthetic CSV contains daily request/error totals and average response times, not individual request latencies or live data. I can show daily trends, service comparisons or two date periods.");
        Evidence = new(nameof(ExplainLimitation), new { status }, Limitation, Limitation);
        return Limitation;
    }

    private void BeginCall()
    {
        // Only one tool call per question: a second call cancels the run and fails it.
        budget.Token.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _calls) > 1)
        {
            budget.Cancel();
            throw new InvalidOperationException("one_exploration_per_question");
        }
    }
}
