namespace MorganHacks.Applications;

/// <summary>
/// The only surface other modules may use. Anything not exposed here is
/// private to Applications.
/// </summary>
/// <remarks>
/// Owns the <c>applications.*</c> schema. No other module queries those
/// tables. The one sanctioned exception in the whole system runs the other
/// way: lark writes a summary row into <c>applications.message_summary</c>,
/// which is a rebuildable projection rather than a second source of truth.
/// </remarks>
public interface IApplications
{
}
