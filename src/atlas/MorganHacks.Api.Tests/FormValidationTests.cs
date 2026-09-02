using MorganHacks.Applications.Forms;

namespace MorganHacks.Api.Tests;

/// <summary>
/// What may be put in front of applicants.
/// </summary>
/// <remarks>
/// Publishing is the moment a form becomes something several hundred people
/// answer, and after which it cannot be corrected for the ones who already did.
/// Everything here is a mistake that is cheap now and unfixable later.
/// </remarks>
public class FormValidationTests
{
    private static List<FormField> ValidForm() => [.. MlhFields.All];

    private static FormField Question(string key, string label = "A question") => new()
    {
        Key = key,
        Type = FieldType.ShortText,
        Label = label,
    };

    [Fact]
    public void The_starting_form_is_publishable()
    {
        // Every form begins with MLH's questions, so a team that changes
        // nothing still has something they are allowed to publish.
        Assert.True(FormValidation.CanPublish(ValidForm()));
    }

    [Fact]
    public void A_form_missing_an_MLH_question_cannot_be_published()
    {
        // The failure this exists for: somebody tidies the form the week before
        // launch and removes an obligation. It surfaces at the export, when
        // several hundred people can no longer be asked again.
        var fields = ValidForm();
        fields.RemoveAll(f => f.Key == "phone");

        Assert.Contains(FormValidation.Check(fields), p => p.FieldKey == "phone");
        Assert.False(FormValidation.CanPublish(fields));
    }

    [Fact]
    public void Two_questions_cannot_share_a_key()
    {
        // The second silently overwrites the first's answer, and nothing about
        // the form looks wrong while it happens.
        var fields = ValidForm();
        fields.Add(Question("favourite_language", "What do you write in?"));
        fields.Add(Question("favourite_language", "Pick a language"));

        Assert.Contains(FormValidation.Check(fields), p => p.Message.Contains("share the key"));
    }

    [Fact]
    public void A_choice_question_needs_something_to_choose_from()
    {
        var fields = ValidForm();
        fields.Add(new FormField { Key = "shirt", Type = FieldType.Select, Label = "Shirt size" });

        Assert.Contains(FormValidation.Check(fields), p => p.FieldKey == "shirt");
    }

    [Fact]
    public void Two_options_cannot_store_the_same_value()
    {
        // Distinct labels, one stored value: the answers are indistinguishable
        // afterwards and no amount of reporting recovers which was meant.
        var fields = ValidForm();
        fields.Add(new FormField
        {
            Key = "diet",
            Type = FieldType.Radio,
            Label = "Dietary needs",
            Options = [new("none", "No restrictions"), new("none", "None")],
        });

        Assert.Contains(FormValidation.Check(fields), p => p.Message.Contains("stored as 'none'"));
    }

    [Fact]
    public void There_can_only_be_one_file_question()
    {
        // An application stores a single resume_key, so a second upload has
        // nowhere to go.
        var fields = ValidForm();
        fields.Add(new FormField { Key = "resume", Type = FieldType.File, Label = "Resume" });
        fields.Add(new FormField { Key = "portfolio", Type = FieldType.File, Label = "Portfolio" });

        Assert.Contains(FormValidation.Check(fields), p => p.Message.Contains("stores one file"));
    }

    [Fact]
    public void An_agreement_cannot_have_options()
    {
        var fields = ValidForm();
        fields.Add(new FormField
        {
            Key = "terms",
            Type = FieldType.Consent,
            Label = "Do you agree?",
            Options = [new("yes", "Yes"), new("no", "No")],
        });

        Assert.Contains(FormValidation.Check(fields), p => p.FieldKey == "terms");
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        // One at a time turns fixing a form into a guessing game where each fix
        // reveals the next complaint.
        var fields = ValidForm();
        fields.RemoveAll(f => f.Key is "phone" or "age");
        fields.Add(new FormField { Key = "x", Type = FieldType.Select, Label = "" });

        Assert.True(FormValidation.Check(fields).Count >= 3);
    }

    [Fact]
    public void MLHs_own_questions_are_locked()
    {
        // The builder reads this to decide what it will let somebody delete.
        Assert.All(MlhFields.All, f => Assert.True(f.Locked));
    }

    [Fact]
    public void The_two_required_agreements_are_stored_as_timestamps()
    {
        // "They agreed" is weaker evidence than "they agreed at 14:03 against
        // form version 3", and this is a legal agreement we may have to show.
        foreach (var key in new[] { "mlh_coc_agreed_at", "mlh_data_sharing_at" })
        {
            var field = MlhFields.All.Single(f => f.Key == key);
            Assert.True(field.Required);
            Assert.Equal(AnswerStorage.Column, field.Storage);
            Assert.EndsWith("_at", field.Column);
        }
    }

    [Fact]
    public void Marketing_consent_is_optional()
    {
        // MLH requires it be offered, not that anybody accepts it.
        Assert.False(MlhFields.All.Single(f => f.Key == "mlh_marketing_opt_in").Required);
    }
}
