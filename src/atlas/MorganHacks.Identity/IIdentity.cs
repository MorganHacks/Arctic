namespace MorganHacks.Identity;

/// <summary>
/// The only surface other modules may use. Anything not exposed here is
/// private to Identity.
/// </summary>
/// <remarks>
/// Cross-module calls go through this interface, wired in DI by
/// <c>MorganHacks.Api</c>. Modules never reference each other directly — that
/// rule is what keeps this extractable into its own service later.
/// </remarks>
public interface IIdentity
{
}
