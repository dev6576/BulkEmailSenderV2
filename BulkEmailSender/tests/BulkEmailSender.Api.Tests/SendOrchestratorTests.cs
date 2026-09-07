using BulkEmailSender.Api.Domain.Email;
using BulkEmailSender.Api.Domain.Sending;
using BulkEmailSender.Api.Services.Email;
using BulkEmailSender.Api.Services.Providers;
using BulkEmailSender.Api.Services.Sending;
using BulkEmailSender.Api.Services.Template;
using BulkEmailSender.Api.Services.Validation;

namespace BulkEmailSender.Api.Tests.Services.Sending;

public sealed class SendOrchestratorTests
{
    [Fact]
    public async Task SendAsync_ValidRows_SendsEveryRow()
    {
        var sentEmails = new List<string>();

        var provider = new StubEmailProvider(
            (recipient, _, _) =>
            {
                sentEmails.Add(recipient);

                return Task.FromResult(
                    EmailSendResult.Success());
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "alice@example.com",
                "Alice"),

            CreateRecipient(
                2,
                "bob@example.com",
                "Bob")
        };

        var results = await orchestrator.SendAsync(
            recipients,
            CreateEmail(),
            CancellationToken.None);

        Assert.Equal(2, results.Count);

        Assert.All(
            results,
            result =>
                Assert.Equal(
                    SendRowStatus.Sent,
                    result.Status));

        Assert.Equal(
            [
                "alice@example.com",
                "bob@example.com"
            ],
            sentEmails);
    }

    [Fact]
    public async Task SendAsync_InvalidRow_DoesNotCallProvider()
    {
        var sentEmails = new List<string>();

        var provider = new StubEmailProvider(
            (recipient, _, _) =>
            {
                sentEmails.Add(recipient);

                return Task.FromResult(
                    EmailSendResult.Success());
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "invalid-email",
                "Alice"),

            CreateRecipient(
                2,
                "bob@example.com",
                "Bob")
        };

        var results = await orchestrator.SendAsync(
            recipients,
            CreateEmail(),
            CancellationToken.None);

        Assert.Equal(2, results.Count);

        Assert.Equal(
            SendRowStatus.Failed,
            results[0].Status);

        Assert.Equal(
            SendRowStatus.Sent,
            results[1].Status);

        Assert.Equal(
            [
                "bob@example.com"
            ],
            sentEmails);
    }

    [Fact]
    public async Task SendAsync_ProviderFailure_DoesNotStopFollowingRows()
    {
        var attemptedEmails = new List<string>();

        var provider = new StubEmailProvider(
            (recipient, _, _) =>
            {
                attemptedEmails.Add(recipient);

                if (recipient == "bob@example.com")
                {
                    return Task.FromResult(
                        EmailSendResult.Failure(
                            "ProviderError",
                            "Simulated provider failure."));
                }

                return Task.FromResult(
                    EmailSendResult.Success());
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "alice@example.com",
                "Alice"),

            CreateRecipient(
                2,
                "bob@example.com",
                "Bob"),

            CreateRecipient(
                3,
                "charlie@example.com",
                "Charlie")
        };

        var results = await orchestrator.SendAsync(
            recipients,
            CreateEmail(),
            CancellationToken.None);

        Assert.Equal(3, results.Count);

        Assert.Equal(
            SendRowStatus.Sent,
            results[0].Status);

        Assert.Equal(
            SendRowStatus.Failed,
            results[1].Status);

        Assert.Equal(
            "ProviderError",
            results[1].ErrorCode);

        Assert.Equal(
            "Simulated provider failure.",
            results[1].ErrorMessage);

        Assert.Equal(
            SendRowStatus.Sent,
            results[2].Status);

        Assert.Equal(
            [
                "alice@example.com",
                "bob@example.com",
                "charlie@example.com"
            ],
            attemptedEmails);
    }

    [Fact]
    public async Task SendAsync_ProviderException_DoesNotStopFollowingRows()
    {
        var attemptedEmails = new List<string>();

        var provider = new StubEmailProvider(
            (recipient, _, _) =>
            {
                attemptedEmails.Add(recipient);

                if (recipient == "bob@example.com")
                {
                    throw new InvalidOperationException(
                        "Simulated provider exception.");
                }

                return Task.FromResult(
                    EmailSendResult.Success());
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "alice@example.com",
                "Alice"),

            CreateRecipient(
                2,
                "bob@example.com",
                "Bob"),

            CreateRecipient(
                3,
                "charlie@example.com",
                "Charlie")
        };

        var results = await orchestrator.SendAsync(
            recipients,
            CreateEmail(),
            CancellationToken.None);

        Assert.Equal(3, results.Count);

        Assert.Equal(
            SendRowStatus.Sent,
            results[0].Status);

        Assert.Equal(
            SendRowStatus.Failed,
            results[1].Status);

        Assert.Equal(
            "UnexpectedError",
            results[1].ErrorCode);

        Assert.Equal(
            SendRowStatus.Sent,
            results[2].Status);

        Assert.Equal(
            [
                "alice@example.com",
                "bob@example.com",
                "charlie@example.com"
            ],
            attemptedEmails);
    }

    [Fact]
    public async Task SendAsync_ProcessesRowsSequentially()
    {
        var executionOrder = new List<string>();

        var provider = new StubEmailProvider(
            async (recipient, _, _) =>
            {
                executionOrder.Add(
                    $"{recipient}-started");

                await Task.Delay(20);

                executionOrder.Add(
                    $"{recipient}-finished");

                return EmailSendResult.Success();
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "alice@example.com",
                "Alice"),

            CreateRecipient(
                2,
                "bob@example.com",
                "Bob"),

            CreateRecipient(
                3,
                "charlie@example.com",
                "Charlie")
        };

        await orchestrator.SendAsync(
            recipients,
            CreateEmail(),
            CancellationToken.None);

        Assert.Equal(
            [
                "alice@example.com-started",
                "alice@example.com-finished",
                "bob@example.com-started",
                "bob@example.com-finished",
                "charlie@example.com-started",
                "charlie@example.com-finished"
            ],
            executionOrder);
    }

    [Fact]
    public async Task SendAsync_CancellationIsPropagated()
    {
        using var cancellationSource =
            new CancellationTokenSource();

        var provider = new StubEmailProvider(
            async (_, _, cancellationToken) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);

                return EmailSendResult.Success();
            });

        var orchestrator = CreateOrchestrator(provider);

        var recipients = new[]
        {
            CreateRecipient(
                1,
                "alice@example.com",
                "Alice")
        };

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                orchestrator.SendAsync(
                    recipients,
                    CreateEmail(),
                    cancellationSource.Token));
    }

    private static SendOrchestrator CreateOrchestrator(
        IEmailProvider provider)
    {
        var templateRenderer =
            new TemplateRenderer();

        var recipientValidator =
            new RecipientValidator();

        var sendRowValidator =
            new SendRowValidator(
                recipientValidator,
                templateRenderer);

        var emailRenderer =
            new EmailRenderer(
                sendRowValidator,
                templateRenderer);

        return new SendOrchestrator(
            emailRenderer,
            provider);
    }

    private static Recipient CreateRecipient(
        long rowId,
        string email,
        string name)
    {
        return new Recipient(
            rowId,
            new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Name"] = name,
                ["Company"] = "Acme"
            });
    }

    private static EmailDefinition CreateEmail()
    {
        return new EmailDefinition
        {
            Subject = "Welcome to {Company}",
            Body = "<p>Hi {Name}</p>"
        };
    }
}