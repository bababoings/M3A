namespace M3A.Domain.Enums;

/// <summary>Status of a ticket. Starts as Issued, then Purchased, then Redeemed.</summary>
public enum TicketStatus
{
    Issued,
    Purchased,
    Redeemed,
}
