using System.Text.Json;
using FluentAssertions;
using HotFixAmbulance.Core;
using HotFixAmbulance.Elastic;
using Xunit;

namespace HotFixAmbulance.UnitTests.Elastic;

public sealed class SerilogDocumentMapperTests
{
    private const string ValidJson = """
    {
      "@timestamp": "2026-06-16T11:30:00.000Z",
      "level": "Error",
      "message": "Boom",
      "fields": {
        "Application": "checkout-api",
        "Version": "1.4.2",
        "ExceptionType": "System.NullReferenceException",
        "RequestMethod": "POST",
        "RequestPath": "/checkout",
        "StatusCode": 500,
        "CorrelationId": "abc-123"
      }
    }
    """;

    [Fact]
    public void TryMap_returns_entry_for_valid_document()
    {
        var element = JsonDocument.Parse(ValidJson).RootElement;

        var entry = InvokeTryMap(element);

        entry.Should().NotBeNull();
        entry!.ApiName.Should().Be("checkout-api");
        entry.Severity.Should().Be(Severity.Error);
        entry.ExceptionType.Should().Be("System.NullReferenceException");
        entry.Endpoint.Should().Be("/checkout");
        entry.RequestMethod.Should().Be("POST");
        entry.HttpStatus.Should().Be(500);
        entry.CorrelationId.Should().Be("abc-123");
        entry.ServiceVersion.Should().Be("1.4.2");
        entry.TimestampUtc.UtcDateTime.Should().Be(new DateTime(2026, 6, 16, 11, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryMap_returns_null_when_application_missing()
    {
        const string json = """
        {
          "@timestamp": "2026-06-16T11:30:00.000Z",
          "level": "Error",
          "fields": { }
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        InvokeTryMap(element).Should().BeNull();
    }

    [Fact]
    public void TryMap_returns_null_when_level_unknown()
    {
        const string json = """
        {
          "@timestamp": "2026-06-16T11:30:00.000Z",
          "level": "Verbose",
          "fields": { "Application": "x" }
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        InvokeTryMap(element).Should().BeNull();
    }

    [Fact]
    public void TryMap_returns_null_when_timestamp_missing()
    {
        const string json = """
        { "level": "Error", "fields": { "Application": "x" } }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        InvokeTryMap(element).Should().BeNull();
    }

    [Fact]
    public void TryMap_extracts_stack_frame_from_pre_extracted_fields()
    {
        const string json = """
        {
          "@timestamp": "2026-06-16T11:30:00.000Z",
          "level": "Error",
          "fields": {
            "Application": "demo-api",
            "ExceptionType": "System.NullReferenceException",
            "StackFile": "BrokenServices.cs",
            "StackSymbol": "OrderProcessor.GetCustomerEmail",
            "StackLine": 54
          }
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        var entry = InvokeTryMap(element);

        entry.Should().NotBeNull();
        entry!.StackFile.Should().Be("BrokenServices.cs");
        entry.StackSymbol.Should().Be("OrderProcessor.GetCustomerEmail");
        entry.StackLine.Should().Be(54);
    }

    [Fact]
    public void TryMap_parses_top_user_frame_from_ecs_error_stack_trace()
    {
        const string json = """
        {
          "@timestamp": "2026-06-16T11:30:00.000Z",
          "level": "Error",
          "fields": { "Application": "demo-api" },
          "error": {
            "stack_trace": "System.NullReferenceException: Object reference not set to an instance of an object.\n   at DemoApi.OrderProcessor.GetCustomerEmail(String customerId) in C:\\repo\\demo-api\\BrokenServices.cs:line 54\n   at DemoApi.OrderProcessor.PlaceOrder(OrderRequest req) in C:\\repo\\demo-api\\BrokenServices.cs:line 62\n   at Program.<>c.<<Main>$>b__0_5(OrderRequest req, OrderProcessor processor, ILogger`1 log) in C:\\repo\\demo-api\\Program.cs:line 74"
          }
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        var entry = InvokeTryMap(element);

        entry.Should().NotBeNull();
        entry!.StackFile.Should().Be("BrokenServices.cs");
        entry.StackSymbol.Should().Be("OrderProcessor.GetCustomerEmail");
        entry.StackLine.Should().Be(54);
    }

    private static LogEntry? InvokeTryMap(JsonElement element)
    {
        // SerilogDocumentMapper is internal; exposed to tests via InternalsVisibleTo.
        var mapperType = typeof(ElasticOptions).Assembly.GetType("HotFixAmbulance.Elastic.SerilogDocumentMapper", throwOnError: true)!;
        var method = mapperType.GetMethod("TryMap", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (LogEntry?)method.Invoke(null, [element]);
    }
}
