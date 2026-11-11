using System.ComponentModel.DataAnnotations;
using Delily.Api;
using Delily.Auth;
using Delily.Protocols.Imap;
using Delily.Protocols.Smtp;
using Delily.Queue;
using Delily.Storage;
using Delily.Transport;

namespace Delily.UnitTests;

public sealed class OptionsValidationTests
{
    [Fact]
    public void DefaultSmtpOptions_AreValid()
    {
        AssertValid(new SmtpServerOptions());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void SmtpPort_MustBeInRange(int port)
    {
        AssertInvalid(new SmtpServerOptions { InboundPort = port });
    }

    [Fact]
    public void SmtpOptions_MaxRecipientsMustBePositive()
    {
        AssertInvalid(new SmtpServerOptions { MaxRecipients = 0 });
    }

    [Fact]
    public void DefaultImapOptions_AreValid()
    {
        AssertValid(new ImapServerOptions());
    }

    [Fact]
    public void StorageOptions_RequireBlobPath()
    {
        AssertInvalid(new StorageOptions { BlobPath = "" });
    }

    [Fact]
    public void QueueOptions_DefaultBatchSizeIsValid()
    {
        AssertValid(new QueueOptions());
    }

    [Fact]
    public void AuthOptions_DefaultMechanismsArePinned()
    {
        var options = new AuthOptions();

        Assert.True(options.RequireTls);
        Assert.Contains("SCRAM-SHA-256", options.AllowedMechanisms);
        Assert.Contains("PLAIN", options.AllowedMechanisms);
    }

    [Fact]
    public void TlsOptions_DefaultToDisabledForLocalDevelopment()
    {
        var options = new TlsOptions();

        Assert.False(options.Enabled);
        Assert.Equal("Tls12", options.MinimumProtocol);
    }

    [Fact]
    public void DefaultHttpSubmissionOptions_AreValid()
    {
        var options = new HttpSubmissionOptions();

        AssertValid(options);
        Assert.True(options.RequireIdempotencyKey);
    }

    [Fact]
    public void HttpSubmissionOptions_RequirePositiveAttachmentLimit()
    {
        AssertInvalid(new HttpSubmissionOptions { MaxAttachments = -1 });
    }

    private static void AssertValid(object options)
    {
        var results = new List<ValidationResult>();

        Assert.True(
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true),
            string.Join("; ", results.Select(result => result.ErrorMessage)));
    }

    private static void AssertInvalid(object options)
    {
        var results = new List<ValidationResult>();

        Assert.False(
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true));
    }
}
