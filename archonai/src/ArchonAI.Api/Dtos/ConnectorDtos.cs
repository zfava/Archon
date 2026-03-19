using ArchonAI.Core.Interfaces;

namespace ArchonAI.Api.Dtos;

public sealed record QuickBooksInvoiceRequest(string CustomerId, IReadOnlyList<QuickBooksLineItem> LineItems);
public sealed record SlackSendMessageRequest(string Channel, string Text, string? ThreadTs = null);
public sealed record SlackAlertRequest(string Channel, string AlertLevel, string Title, string Details);
public sealed record GmailSendRequest(string To, string Subject, string Body, bool IsHtml = false);
public sealed record GoogleDocCreateRequest(string Title, string? Content = null);
public sealed record GoogleSheetWriteRequest(string Range, IReadOnlyList<IReadOnlyList<string>> Values);
public sealed record M365SendEmailRequest(string To, string Subject, string Body, bool IsHtml = false);
public sealed record M365TeamsMessageRequest(string Content);
