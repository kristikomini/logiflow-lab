using System.Globalization;
using LogiFlow.Domain.Results;

namespace LogiFlow.Domain.Orders;

/// <summary>
/// The human-facing order reference, e.g. <c>ORD-2026-000417</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why does this exist when the order already has an <see cref="OrderId"/>?</b>
/// Because a GUID is unusable by humans. Nobody reads
/// <c>0193f8a2-1c4d-7890-abcd-ef0123456789</c> down a phone line to a support agent.
/// Real systems carry two identifiers, and conflating them causes trouble both ways:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Surrogate key</b> (<see cref="OrderId"/>) — GUID, generated client-side, never shown
///     to anyone, used for every foreign key and lookup.
///   </description></item>
///   <item><description>
///     <b>Natural/business key</b> (this type) — short, sequential, printable, quoted on
///     invoices, unique-indexed.
///   </description></item>
/// </list>
/// <para>
/// Using a sequential business number as the <i>primary key</i> is the trap: it forces a
/// database round trip before you can build the aggregate, it leaks your order volume to
/// competitors (the classic "order #47" problem), and it makes sharding painful.
/// </para>
/// </remarks>
public readonly record struct OrderNumber
{
    private OrderNumber(string value) => Value = value;

    /// <summary>The formatted reference.</summary>
    public string Value { get; }

    /// <summary>Builds a reference from a year and a per-year sequence value.</summary>
    /// <param name="year">Calendar year the order was placed.</param>
    /// <param name="sequence">Monotonic counter within that year, from a database sequence.</param>
    public static Result<OrderNumber> Create(int year, int sequence)
    {
        if (year is < 2000 or > 2999)
        {
            return Error.Validation("OrderNumber.InvalidYear", "Year must be between 2000 and 2999.");
        }

        if (sequence is < 1 or > 999_999)
        {
            return Error.Validation("OrderNumber.SequenceOutOfRange", "Sequence must be between 1 and 999999.");
        }

        return new OrderNumber(
            string.Create(CultureInfo.InvariantCulture, $"ORD-{year:0000}-{sequence:000000}"));
    }

    /// <summary>Rehydrates from trusted storage.</summary>
    public static OrderNumber FromTrusted(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
