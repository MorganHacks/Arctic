namespace MorganHacks.Identity.Services;

/// <summary>
/// Sends one transactional email.
/// </summary>
/// <remarks>
/// An interface rather than a direct call so that the Identity module never
/// learns how mail actually leaves the building. What it takes — an address, a
/// person, and a link — is everything a sender needs and nothing about how it
/// sends.
/// </remarks>
public interface IEmailSender
{
    /// <param name="personId">
    /// Carried so the queued message can point at a person. Without it the
    /// support answer to "did they get their link" is a search by address,
    /// which is the query we least want to make easy.
    /// </param>
    Task SendMagicLinkAsync(
        Guid personId, string email, string link, CancellationToken ct = default);

    /// <summary>
    /// Tells somebody they can now use the organizer console.
    /// </summary>
    /// <remarks>
    /// Sent when a person joins their first team, not when their address is
    /// added. Being on the allowlist grants nothing, so an email at that
    /// moment would be true and would read as a broken account.
    /// <para>
    /// The address is in the body as well as in the envelope on purpose. The
    /// failure this exists to prevent is somebody signing in with the wrong
    /// Google account, and the refusal they would get cannot explain itself
    /// without telling strangers which addresses are on the allowlist.
    /// </para>
    /// </remarks>
    /// <param name="console">Where the console is, absolute.</param>
    Task SendOrganizerWelcomeAsync(
        Guid personId, string email, string console, CancellationToken ct = default);
}
