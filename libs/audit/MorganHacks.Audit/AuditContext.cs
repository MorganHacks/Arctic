using Npgsql;

namespace MorganHacks.Audit;

/// <summary>
/// Tells a transaction who is acting, so the audit triggers can record it.
/// </summary>
/// <remarks>
/// The database writes the trail. It knows what changed, because it is the
/// thing being changed; it cannot know who asked, because a connection is not
/// a person. This is the one channel that carries that across, and it is the
/// same channel <c>0006_applications_triggers.sql</c> already uses for status
/// history — one convention, so somebody debugging a missing actor has one
/// place to look rather than two.
/// <para>
/// A transaction is required rather than optional. The setting is written with
/// <c>is_local</c>, so it survives exactly as long as the transaction and
/// cannot leak onto the next request that borrows the same pooled connection.
/// Outside a transaction each statement is its own, and the setting would be
/// gone before the statement it was set for — silently, producing a trail that
/// says every change was made by nobody.
/// </para>
/// </remarks>
public static class AuditContext
{
    /// <summary>
    /// The transaction-local setting the triggers read.
    /// </summary>
    /// <remarks>
    /// Named here rather than spelled out at each call site: the failure mode
    /// of a typo is not an error but a null actor, which looks exactly like a
    /// change somebody made by hand.
    /// </remarks>
    public const string ActorSetting = "app.actor_id";

    /// <summary>
    /// Names the person responsible for everything this transaction goes on to
    /// do.
    /// </summary>
    /// <remarks>
    /// <paramref name="actorId"/> is nullable because some actions genuinely
    /// have nobody behind them — an expiry sweep, an import, a seed. Passing
    /// null records null, which is the honest answer; inventing a person for
    /// those would put a name against decisions they did not make, and make
    /// the trail's actor column worth nothing on the rows where it matters.
    /// </remarks>
    public static async Task SetActorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? actorId,
        CancellationToken ct = default)
    {
        // set_config takes text, so null is the empty string here and the
        // trigger's NULLIF turns it back into a NULL actor. Npgsql cannot bind
        // a null uuid through a text parameter without a cast either way.
        await using var cmd = new NpgsqlCommand(
            "SELECT set_config(@setting, @actor, true)", connection, transaction);
        cmd.Parameters.AddWithValue("setting", ActorSetting);
        cmd.Parameters.AddWithValue("actor", actorId?.ToString() ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
