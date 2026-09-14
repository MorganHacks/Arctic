using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Tests;

/// <summary>
/// What an inbox shows in the sender column.
/// </summary>
/// <remarks>
/// Worth its own file because it is the one part of an email that is read
/// before the email is opened, and because the header it goes into has
/// punctuation of its own. A name that breaks the header does not fail
/// loudly — it produces a From line that some clients parse one way and
/// others another.
/// </remarks>
public class SenderNameTests
{
    private static EmailTemplate Template(string? fromName) =>
        new(Guid.NewGuid(), "test", "broadcast", "hi", "<p>hi</p>", "hi",
            "mail", "morganhacks.com", null, fromName);

    [Fact]
    public void A_name_is_put_in_front_of_the_address()
    {
        // The whole point: without this the client has only the local part to
        // show, so mail@morganhacks.com arrives from somebody called "mail".
        //
        // Quoted even though this name does not need it. MailAddress quotes
        // every display name, which is valid and which every client strips
        // before showing it — and a rule that quotes always is one nobody has
        // to check against the grammar for atoms every time a name changes.
        Assert.Equal(
            "\"MorganHacks\" <mail@morganhacks.com>", Template("MorganHacks").From);
    }

    [Fact]
    public void No_name_leaves_the_address_on_its_own()
    {
        // A bare address is a legitimate choice and has to stay valid. Wrapping
        // it in empty angle brackets would be a header some providers reject.
        Assert.Equal("mail@morganhacks.com", Template(null).From);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Whitespace_is_not_a_name(string name)
    {
        // A template saved with a space in the field would otherwise send from
        // <code>" " &lt;mail@…&gt;</code>, which shows as a blank sender.
        Assert.Equal("mail@morganhacks.com", Template(name).From);
    }

    [Fact]
    public void A_name_with_the_headers_own_punctuation_is_quoted()
    {
        // "MorganHacks, Inc." is the name somebody types without thinking about
        // RFC 5322, and an unquoted comma in a From header starts a second
        // address.
        Assert.Equal(
            "\"MorganHacks, Inc.\" <mail@morganhacks.com>",
            Template("MorganHacks, Inc.").From);
    }

    [Fact]
    public void The_address_is_still_available_on_its_own()
    {
        // Some things need the address without the name — a bounce lookup, a
        // suppression check — and building the header only to strip it again
        // is how the two come to disagree.
        Assert.Equal("mail@morganhacks.com", Template("MorganHacks").Address);
    }
}
