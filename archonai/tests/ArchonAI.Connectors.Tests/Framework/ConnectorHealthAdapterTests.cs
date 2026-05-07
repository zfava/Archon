using System.Net;
using ArchonAI.Connectors.Framework;
using ArchonAI.Connectors.Salesforce;
using ArchonAI.Connectors.Tests.Shared;
using ArchonAI.Core.Interfaces;
using ArchonAI.Core.Models;
using ArchonAI.Core.Models.Connector;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ArchonAI.Connectors.Tests.Framework;

public sealed class ConnectorHealthAdapterTests
{
    [Fact]
    public void ClassifyHttpError_Maps401_ToAuthentication()
    {
        ConnectorHealthAdapter.ClassifyHttpError(401).Should().Be(ConnectorErrorCategory.Authentication);
    }

    [Fact]
    public void ClassifyHttpError_Maps403_ToAuthorization()
    {
        ConnectorHealthAdapter.ClassifyHttpError(403).Should().Be(ConnectorErrorCategory.Authorization);
    }

    [Fact]
    public void ClassifyHttpError_Maps404_ToNotFound()
    {
        ConnectorHealthAdapter.ClassifyHttpError(404).Should().Be(ConnectorErrorCategory.NotFound);
    }

    [Fact]
    public void ClassifyHttpError_Maps429_ToRateLimit()
    {
        ConnectorHealthAdapter.ClassifyHttpError(429).Should().Be(ConnectorErrorCategory.RateLimit);
    }

    [Fact]
    public void ClassifyHttpError_Maps408_ToTimeout()
    {
        ConnectorHealthAdapter.ClassifyHttpError(408).Should().Be(ConnectorErrorCategory.Timeout);
    }

    [Fact]
    public void ClassifyHttpError_Maps500_ToServerError()
    {
        ConnectorHealthAdapter.ClassifyHttpError(500).Should().Be(ConnectorErrorCategory.ServerError);
    }

    [Fact]
    public void ClassifyHttpError_Maps503_ToServerError()
    {
        ConnectorHealthAdapter.ClassifyHttpError(503).Should().Be(ConnectorErrorCategory.ServerError);
    }

    [Fact]
    public void ClassifyHttpError_Maps409_ToConflict()
    {
        ConnectorHealthAdapter.ClassifyHttpError(409).Should().Be(ConnectorErrorCategory.Conflict);
    }

    [Fact]
    public void ClassifyHttpError_Maps422_ToInvalidRequest()
    {
        ConnectorHealthAdapter.ClassifyHttpError(422).Should().Be(ConnectorErrorCategory.InvalidRequest);
    }

    [Fact]
    public void GetHealthReport_UnauthenticatedConnector_ReturnsNotConfigured()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var eventBus = Substitute.For<IEventBus>();
        var options = Options.Create(new SalesforceOptions());

        var connector = new SalesforceConnector(httpClient, eventBus,
            NullLogger<SalesforceConnector>.Instance, options);

        var report = ConnectorHealthAdapter.GetHealthReport(connector);

        report.ConnectorName.Should().Be("salesforce");
        report.Status.Should().Be(ConnectorHealthStatus.NotConfigured);
        report.IsAuthenticated.Should().BeFalse();
        report.TotalRequests.Should().Be(0);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetHealthReport_AfterAuth_ReturnsHealthy()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetResponse(HttpStatusCode.OK,
            """{"access_token":"test-token","instance_url":"https://test.salesforce.com"}""");

        var httpClient = new HttpClient(mockHandler);
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<SystemEvent>(), Arg.Any<CancellationToken>())
            .Returns(global::System.Threading.Tasks.Task.CompletedTask);
        var options = Options.Create(new SalesforceOptions { ClientId = "test", ClientSecret = "test" });

        var connector = new SalesforceConnector(httpClient, eventBus,
            NullLogger<SalesforceConnector>.Instance, options);

        await connector.AuthenticateAsync();
        var report = ConnectorHealthAdapter.GetHealthReport(connector);

        report.ConnectorName.Should().Be("salesforce");
        report.Status.Should().Be(ConnectorHealthStatus.Healthy);
        report.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void GetHealthReport_UnknownConnector_ReturnsDefault()
    {
        var connector = Substitute.For<IConnector>();
        connector.SystemName.Returns("custom-connector");

        var report = ConnectorHealthAdapter.GetHealthReport(connector);

        report.ConnectorName.Should().Be("custom-connector");
        report.Status.Should().Be(ConnectorHealthStatus.Unknown);
        report.StatusMessage.Should().Contain("No health reporting available");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ValidateCredentials_FailedAuth_ReturnsInvalid()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.SetResponse(HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"authentication failure"}""");

        var httpClient = new HttpClient(mockHandler);
        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<SystemEvent>(), Arg.Any<CancellationToken>())
            .Returns(global::System.Threading.Tasks.Task.CompletedTask);
        var options = Options.Create(new SalesforceOptions());

        var connector = new SalesforceConnector(httpClient, eventBus,
            NullLogger<SalesforceConnector>.Instance, options);

        var status = await ConnectorHealthAdapter.ValidateCredentialsAsync(connector);

        status.ConnectorName.Should().Be("salesforce");
        status.IsValid.Should().BeFalse();
    }

    [Fact]
    public async global::System.Threading.Tasks.Task ValidateCredentials_UnknownConnector_ReturnsUnsupported()
    {
        var connector = Substitute.For<IConnector>();
        connector.SystemName.Returns("custom");

        var status = await ConnectorHealthAdapter.ValidateCredentialsAsync(connector);

        status.ConnectorName.Should().Be("custom");
        status.IsConfigured.Should().BeFalse();
        status.ValidationError.Should().Contain("does not support");
    }
}
