using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Forms;

public static class MlhSeason
{
    public static int? For(DateTimeOffset? eventStartsAt)
    {
        if (eventStartsAt is null) return null;
        var date = EventZone.Local(eventStartsAt.Value);
        return date.Month >= 7 ? date.Year + 1 : date.Year;
    }
}
