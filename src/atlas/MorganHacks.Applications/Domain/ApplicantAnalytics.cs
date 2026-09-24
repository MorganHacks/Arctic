using MorganHacks.Applications.Forms;

namespace MorganHacks.Applications.Domain;

public sealed record AnalyticsBucket(string Label, int Count);
public sealed record ApplicationActivity(DateOnly Date, int Started, int Submitted);
public sealed record ApplicantDemographics(
    bool GenderCollected,
    IReadOnlyList<AnalyticsBucket> Gender,
    IReadOnlyList<AnalyticsBucket> Education,
    IReadOnlyList<AnalyticsBucket> Experience);
public sealed record ApplicantAnalytics(
    int TotalApplicants,
    int SubmittedApplications,
    IReadOnlyDictionary<string, int> Statuses,
    IReadOnlyList<AnalyticsBucket> Schools,
    IReadOnlyList<ApplicationActivity> Activity,
    ApplicantDemographics? Demographics);

public static class AnalyticsAnswers
{
    public static FormField? GenderField(IReadOnlyList<FormField> fields) => fields.FirstOrDefault(field =>
        field.Storage == AnswerStorage.Responses
        && field.Type is FieldType.Select or FieldType.Radio or FieldType.ShortText
        && (Normalize(field.Key) is "gender" or "genderidentity"
            || Normalize(field.Label) is "gender" or "genderidentity" or "whatisyourgender"
                or "whatisyourgenderidentity" or "whatgenderdoyouidentifywith"));

    public static string Gender(string? value, FormField? field)
    {
        if (field is null || string.IsNullOrWhiteSpace(value)) return "Not provided";
        var label = OptionLabel(value, field);
        return Normalize(label) switch
        {
            "male" or "man" or "m" => "Men",
            "female" or "woman" or "f" => "Women",
            "nonbinary" or "genderqueer" or "gendernonconforming" => "Non-binary",
            "prefernottosay" or "prefernottoanswer" or "declinetoanswer" => "Prefer not to answer",
            _ => "Self-described",
        };
    }

    public static string OptionLabel(string? value, FormField? field) => string.IsNullOrWhiteSpace(value)
        ? "Not provided"
        : field?.Options.FirstOrDefault(option => option.Value == value)?.Label ?? value.Trim();

    private static string Normalize(string value) => new(value.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
}
