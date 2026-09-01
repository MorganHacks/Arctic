using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Lark.Sending;

/// <summary>
/// Sends through Amazon SES.
/// </summary>
/// <remarks>
/// Every failure is turned into a <see cref="SendOutcome"/> rather than
/// allowed to escape. The queue decides what a failure means — retry,
/// suppress, or give up — and it can only do that if it is told about every
/// failure in the same shape.
/// <para>
/// While the account is in the SES sandbox this succeeds only for verified
/// recipients and returns an ordinary refusal for everyone else. That is the
/// same code path production uses; leaving the sandbox widens who can be
/// mailed and changes nothing here.
/// </para>
/// </remarks>
public sealed class SesEmailProvider(
    IAmazonSimpleEmailServiceV2 ses,
    ILogger<SesEmailProvider> log) : IEmailProvider
{
    public async Task<SendOutcome> SendAsync(
        ClaimedMessage message, CancellationToken ct = default)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = message.From,
            Destination = new Destination { ToAddresses = [message.ToEmail] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Html = new Content { Data = message.BodyHtml, Charset = "UTF-8" },
                        Text = new Content { Data = message.BodyText, Charset = "UTF-8" },
                    },
                },
            },
        };

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
        {
            request.ReplyToAddresses = [message.ReplyTo];
        }

        try
        {
            var response = await ses.SendEmailAsync(request, ct);
            return SendOutcome.Sent(response.MessageId);
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            // The address is never logged. Knowing a send failed is
            // operationally useful; storing who it was for is not worth the
            // PII sitting in a log aggregator.
            log.LogWarning(
                "SES refused a message: {ErrorCode} {Status}", ex.ErrorCode, ex.StatusCode);

            return SendOutcome.Refused(
                ex.Message, (int)ex.StatusCode, ex.ErrorCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unrecognised failures are classified as temporary further down,
            // which is the right way round: a few pointless retries cost
            // little, while wrongly suppressing means somebody silently never
            // hears from us.
            log.LogWarning(ex, "A send failed before SES answered.");
            return SendOutcome.Refused(ex.Message);
        }
    }
}
