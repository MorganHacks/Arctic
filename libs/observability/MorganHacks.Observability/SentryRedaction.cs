using Sentry;
using Sentry.Extensibility;

namespace MorganHacks.Observability;

/// <summary>
/// Strips PII from an error report before it leaves the process.
/// </summary>
/// <remarks>
/// Sentry keeps far more context than a log line does — the request, its query
/// string, breadcrumbs leading up to the failure, and every tag and extra
/// attached along the way. All of it is somewhere an address can hide.
/// </remarks>
public static class SentryRedaction
{
    public static SentryEvent? Scrub(SentryEvent evt, SentryHint hint)
    {
        foreach (var key in evt.Extra.Keys.ToList())
        {
            if (Redaction.SensitiveKeys.Contains(key))
            {
                evt.SetExtra(key, Redaction.Placeholder);
            }
            else if (evt.Extra[key] is string text)
            {
                evt.SetExtra(key, Redaction.Mask(text));
            }
        }

        foreach (var (key, value) in evt.Tags.ToList())
        {
            evt.SetTag(key, Redaction.SensitiveKeys.Contains(key)
                ? Redaction.Placeholder
                : Redaction.Mask(value) ?? string.Empty);
        }

        if (evt.Request is { } request)
        {
            // A magic-link token lives in a query string. An error report that
            // captures one is a working sign-in sitting in an error tracker.
            request.QueryString = null;
            request.Data = null;
            request.Cookies = null;

            foreach (var header in request.Headers.Keys.ToList())
            {
                if (Redaction.SensitiveKeys.Contains(header))
                {
                    request.Headers[header] = Redaction.Placeholder;
                }
            }
        }

        if (evt.Message?.Formatted is { } formatted)
        {
            evt.Message.Formatted = Redaction.Mask(formatted);
        }

        return evt;
    }
}
