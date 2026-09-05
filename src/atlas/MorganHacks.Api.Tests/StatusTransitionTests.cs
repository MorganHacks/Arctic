using MorganHacks.Applications.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The lifecycle rules, with no database involved.
/// </summary>
public class StatusTransitionTests
{
    [Fact]
    public void Every_status_survives_a_round_trip_through_storage()
    {
        // The stored spellings are written out by hand so that renaming a C#
        // member cannot silently change what a column means in rows that
        // already exist. That only holds if every member is actually covered.
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            Assert.Equal(status, ApplicationStatuses.Parse(status.ToWire()));
        }
    }

    [Fact]
    public void An_unknown_stored_status_throws_rather_than_defaulting()
    {
        // A status we cannot name is one we cannot reason about. Quietly
        // treating it as Incomplete would mean deciding an application on a
        // value we did not understand.
        Assert.Throws<ArgumentException>(() => ApplicationStatuses.Parse("shortlisted"));
    }

    [Fact]
    public void A_rejected_application_is_terminal()
    {
        // Reversing a rejection is a new application, not an edit. The history
        // has to keep saying what actually happened.
        foreach (var target in Enum.GetValues<ApplicationStatus>())
        {
            Assert.False(StatusTransition.IsAllowed(ApplicationStatus.Rejected, target));
        }
    }

    [Fact]
    public void Anything_before_check_in_can_be_withdrawn()
    {
        // People ask to be removed at any point, and the answer is always yes
        // until they have physically arrived.
        ApplicationStatus[] before =
        [
            ApplicationStatus.Incomplete, ApplicationStatus.Submitted,
            ApplicationStatus.UnderReview, ApplicationStatus.Waitlisted,
            ApplicationStatus.Accepted, ApplicationStatus.Confirmed,
        ];

        Assert.All(before, s =>
            Assert.True(StatusTransition.IsAllowed(s, ApplicationStatus.Withdrawn)));
    }

    [Fact]
    public void An_expired_rsvp_can_be_reinstated_but_only_deliberately()
    {
        // Somebody who missed the deadline and got in touch. Nothing
        // un-expires on its own.
        Assert.True(StatusTransition.IsAllowed(
            ApplicationStatus.Expired, ApplicationStatus.Accepted));
        Assert.Single(StatusTransition.From(ApplicationStatus.Expired));
    }

    [Fact]
    public void An_application_cannot_skip_review()
    {
        // Accepting straight from submitted would mean a decision with nobody
        // recorded as having looked at it.
        Assert.False(StatusTransition.IsAllowed(
            ApplicationStatus.Submitted, ApplicationStatus.Accepted));
    }

    [Fact]
    public void Checking_in_requires_them_to_have_confirmed()
    {
        // Accepted is an offer, confirmed is an answer. Only the second means
        // we are expecting them.
        Assert.False(StatusTransition.IsAllowed(
            ApplicationStatus.Accepted, ApplicationStatus.CheckedIn));
        Assert.True(StatusTransition.IsAllowed(
            ApplicationStatus.Confirmed, ApplicationStatus.CheckedIn));
    }

    [Fact]
    public void Nothing_transitions_to_itself()
    {
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            Assert.False(StatusTransition.IsAllowed(status, status));
        }
    }
}

/// <summary>What the applicant is told, which is not what we record.</summary>
public class ApplicantViewTests
{
    [Fact]
    public void Submitted_and_under_review_are_indistinguishable()
    {
        // They mean nothing different to an applicant, and the difference
        // would tell them we had started reading.
        Assert.Equal(
            ApplicantView.Describe(ApplicationStatus.Submitted),
            ApplicantView.Describe(ApplicationStatus.UnderReview));
    }

    [Fact]
    public void A_decision_is_hidden_until_it_is_announced()
    {
        // The reason reviewers can work through the queue for a week while
        // every applicant still reads the same thing.
        var undecided = ApplicantView.Describe(ApplicationStatus.Submitted);

        Assert.Equal(undecided, ApplicantView.Describe(ApplicationStatus.Accepted));
        Assert.Equal(undecided, ApplicantView.Describe(ApplicationStatus.Rejected));
        Assert.Equal(undecided, ApplicantView.Describe(ApplicationStatus.Waitlisted));
    }

    [Fact]
    public void Once_announced_a_decision_is_shown()
    {
        Assert.NotEqual(
            ApplicantView.Describe(ApplicationStatus.Submitted),
            ApplicantView.Describe(ApplicationStatus.Rejected, decisionsAnnounced: true));
    }

    [Fact]
    public void An_accepted_applicant_is_told_their_deadline()
    {
        // A confirm-by date with no date on it is how a spot goes unclaimed.
        //
        // The instant is the one a real deadline is set at: 11:59 PM on the
        // 15th in the event's zone, which is the 16th at 04:59 in UTC. Read
        // off the instant as it arrives from the database, the applicant is
        // told the 16th — a day later than the team set, a day later than the
        // email said, and a day after the spot has gone.
        var text = ApplicantView.Describe(
            ApplicationStatus.Accepted,
            decisionsAnnounced: true,
            rsvpDeadline: new DateTimeOffset(2027, 1, 16, 4, 59, 0, TimeSpan.Zero));

        Assert.Contains("January 15", text);
        Assert.DoesNotContain("January 16", text);
    }

    [Fact]
    public void The_deadline_and_the_next_step_never_name_different_days()
    {
        // Both sentences are on the same screen, one under the other. They
        // read the same field and must format it the same way; the failure
        // being one of the two left on the raw instant.
        var deadline = new DateTimeOffset(2027, 1, 16, 4, 59, 0, TimeSpan.Zero);

        Assert.Contains(
            "January 15",
            ApplicantView.Describe(
                ApplicationStatus.Accepted, decisionsAnnounced: true, rsvpDeadline: deadline));
        Assert.Contains(
            "January 15",
            ApplicantView.NextStep(
                ApplicationStatus.Accepted, decisionsAnnounced: true, rsvpDeadline: deadline));
    }

    [Fact]
    public void A_deadline_in_daylight_saving_is_read_on_the_other_offset()
    {
        // The project has been caught by this once already: a September
        // deadline written up as EST when the month was on EDT. Named zone
        // rather than a fixed offset, so the switch is handled rather than
        // assumed — 03:59 UTC is the previous evening only while the clocks
        // are forward.
        var text = ApplicantView.Describe(
            ApplicationStatus.Accepted,
            decisionsAnnounced: true,
            rsvpDeadline: new DateTimeOffset(2027, 9, 16, 3, 59, 0, TimeSpan.Zero));

        Assert.Contains("September 15", text);
    }

    [Fact]
    public void No_internal_vocabulary_reaches_the_applicant()
    {
        // Some words are shared on purpose — an applicant who is waitlisted is
        // told "Waitlisted", and that is the right word. What must never
        // appear is our own spelling: under_review and checked_in are how the
        // column reads, not how a person should be addressed.
        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            var shown = ApplicantView.Describe(status, decisionsAnnounced: true);

            Assert.DoesNotContain("_", shown);
            Assert.False(string.IsNullOrWhiteSpace(shown));
        }
    }

    [Fact]
    public void Declining_and_withdrawing_read_the_same()
    {
        // Whether they turned us down or asked to be removed is our
        // distinction to keep, not something to reflect back at them.
        Assert.Equal(
            ApplicantView.Describe(ApplicationStatus.Declined, decisionsAnnounced: true),
            ApplicantView.Describe(ApplicationStatus.Withdrawn, decisionsAnnounced: true));
    }
}
