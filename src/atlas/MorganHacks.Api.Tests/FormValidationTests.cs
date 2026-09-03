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

    private static FormField PageBreak(string key, string label = "Page two") => new()
    {
        Key = key,
        Type = FieldType.Section,
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
    public void A_survey_does_not_have_to_carry_the_MLH_questions()
    {
        // They are questions about people coming to the event. A mentor
        // sign-up or a feedback survey is not that, and demanding a code of
        // conduct agreement on one made the builder useless for anything but
        // the application form.
        List<FormField> survey =
        [
            new()
            {
                Key = "why_mentor",
                Type = FieldType.Paragraph,
                Label = "Why do you want to mentor?",
                Required = true,
            },
        ];

        Assert.Empty(FormValidation.Check(survey, requiresMlh: false));
        Assert.True(FormValidation.CanPublish(survey, requiresMlh: false));
    }

    [Fact]
    public void An_application_still_has_to()
    {
        // The other half. Loosening this for surveys must not loosen it where
        // the obligation is real.
        List<FormField> application =
        [
            new()
            {
                Key = "why_apply",
                Type = FieldType.Paragraph,
                Label = "Why do you want to come?",
                Required = true,
            },
        ];

        Assert.NotEmpty(FormValidation.Check(application));
        Assert.False(FormValidation.CanPublish(application));
    }

    [Fact]
    public void The_MLH_questions_do_not_collide_with_each_other()
    {
        // Several MLH fields store in columns, and this check would be
        // worthless if the required set tripped it. It also caught a mistake in
        // the test below, which reached for 'email' without noticing MLH had
        // already claimed it.
        Assert.DoesNotContain(
            FormValidation.Check(ValidForm()),
            p => p.Message.Contains("stores in the column"));
    }

    [Fact]
    public void Two_questions_cannot_store_in_the_same_column()
    {
        // The duplicate-key failure one level down, and harder to spot: these
        // two questions have different keys, look unrelated on the form, and
        // land on top of each other in the table. Whoever reads the export sees
        // one answer where two people gave two, with nothing marking the loss.
        var fields = ValidForm();
        fields.Add(new FormField
        {
            Key = "shirt",
            Type = FieldType.Select,
            Label = "What size shirt?",
            Options = [new FieldOption("m", "Medium")],
            Storage = AnswerStorage.Column,
            Column = "shirt_size",
        });
        fields.Add(new FormField
        {
            Key = "tee",
            Type = FieldType.Select,
            Label = "Pick a shirt",
            Options = [new FieldOption("l", "Large")],
            Storage = AnswerStorage.Column,
            Column = "shirt_size",
        });

        var problems = FormValidation.Check(fields);

        // Both are named, because either one moving fixes it and the person
        // reading this has no way to know which one is the mistake.
        Assert.Contains(problems, p => p.FieldKey == "shirt");
        Assert.Contains(problems, p => p.FieldKey == "tee");
        Assert.False(FormValidation.CanPublish(fields));
    }

    [Fact]
    public void Two_questions_may_store_in_different_columns()
    {
        // The guard against a check that simply refuses column storage.
        var fields = ValidForm();
        fields.Add(new FormField
        {
            Key = "shirt",
            Type = FieldType.Select,
            Label = "What size shirt?",
            Options = [new FieldOption("m", "Medium")],
            Storage = AnswerStorage.Column,
            Column = "shirt_size",
        });
        fields.Add(new FormField
        {
            Key = "dietary",
            Type = FieldType.Paragraph,
            Label = "Anything we should know about food?",
            Storage = AnswerStorage.Column,
            Column = "dietary_notes",
        });

        Assert.True(FormValidation.CanPublish(fields));
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


    // ------------------------------------------------------------ sections ---

    [Fact]
    public void A_page_break_may_sit_among_the_questions()
    {
        // The guard against a check that simply refuses the new type. A form
        // split into pages is the ordinary case this exists for.
        var fields = ValidForm();
        fields.Add(PageBreak("section_about"));
        fields.Add(Question("why_apply", "Why do you want to come?"));

        Assert.True(FormValidation.CanPublish(fields));
    }

    [Fact]
    public void A_page_break_cannot_be_required()
    {
        // Nothing is ever answered under it, so a required one is a question
        // nobody can satisfy. Refused here rather than skipped at submit: an
        // ignored rule is one the author believes is doing something.
        var fields = ValidForm();
        fields.Add(PageBreak("section_about") with { Required = true });

        Assert.Contains(
            FormValidation.Check(fields),
            p => p.FieldKey == "section_about" && p.Message.Contains("cannot be required"));
        Assert.False(FormValidation.CanPublish(fields));
    }

    [Fact]
    public void A_page_break_cannot_have_options()
    {
        // There is nothing to choose. Options on a heading are a
        // misunderstanding of what was inserted, the same as options on an
        // agreement.
        var fields = ValidForm();
        fields.Add(PageBreak("section_about") with
        {
            Options = [new FieldOption("yes", "Yes")],
        });

        Assert.Contains(
            FormValidation.Check(fields),
            p => p.FieldKey == "section_about" && p.Message.Contains("cannot have options"));
    }

    [Fact]
    public void A_page_break_cannot_be_pointed_at_a_column()
    {
        // It has no answer, so a column for it would either stay empty forever
        // or shadow the question that genuinely writes there.
        var fields = ValidForm();
        fields.Add(PageBreak("section_about") with
        {
            Storage = AnswerStorage.Column,
            Column = "dietary_notes",
        });

        Assert.Contains(
            FormValidation.Check(fields),
            p => p.FieldKey == "section_about" && p.Message.Contains("no answer to store"));
    }

    [Fact]
    public void A_page_break_needs_a_heading()
    {
        // The heading is the whole of what the page shows above its questions.
        // An empty one is a page that opens with nothing on it.
        var fields = ValidForm();
        fields.Add(PageBreak("section_about", label: " "));

        Assert.Contains(
            FormValidation.Check(fields),
            p => p.FieldKey == "section_about" && p.Message.Contains("no heading"));
    }

    [Fact]
    public void A_form_that_is_only_page_breaks_cannot_be_published()
    {
        // Pages of headings and nothing to fill in. It reads as a working form
        // right up until somebody asks where the answers went.
        List<FormField> headings = [PageBreak("section_one"), PageBreak("section_two", "Page three")];

        Assert.Contains(
            FormValidation.Check(headings, requiresMlh: false),
            p => p.Message.Contains("does not ask anything"));
        Assert.False(FormValidation.CanPublish(headings, requiresMlh: false));
    }

    [Fact]
    public void A_form_with_no_fields_at_all_cannot_be_published_either()
    {
        // The same rule from the other side. A survey with nothing on it is a
        // link that collects nothing.
        Assert.False(FormValidation.CanPublish([], requiresMlh: false));
    }
}
