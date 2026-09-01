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
}
