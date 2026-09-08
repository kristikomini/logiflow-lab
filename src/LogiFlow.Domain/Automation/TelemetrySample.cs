namespace LogiFlow.Domain.Automation;

/// <summary>
/// How much you should believe a reading. This is not "did the request work".
/// </summary>
/// <remarks>
/// <para>
/// A sensor is allowed to say <i>I do not know</i>, and that is information rather than an
/// error. The value that comes with <see cref="Uncertain"/> or <see cref="Bad"/> must never be
/// averaged with real ones: an hour of thermocouple dropouts recorded as <c>0 °C</c> drags a
/// shift average down by a plausible amount, produces no error anywhere, and is never found.
/// </para>
/// <para>
/// Modbus has no equivalent of this at all, so a Modbus adapter is <i>inventing</i> the quality
/// it reports. The honest invention is staleness: a value that has not refreshed within its
/// expected period is <see cref="Uncertain"/>, not <see cref="Good"/> — the absence of an error
/// is not evidence that anybody looked.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/03-telemetry-and-backpressure.md</c>
/// </remarks>
public enum Quality
{
    /// <summary>The device stands behind this value.</summary>
    Good = 0,

    /// <summary>The device produced it but does not vouch for it — stale, out of range, degraded.</summary>
    Uncertain = 1,

    /// <summary>Not a reading. A failed sensor, a dropped connection, a device that stopped answering.</summary>
    Bad = 2,
}

/// <summary>
/// One observation from the machine layer: a tag, a value, how much to believe it, and
/// <b>both</b> of the times it happened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why two timestamps and not one.</b> <see cref="SourceTimestampUtc"/> is when the machine
/// says the value was true — a PLC scan that finished some milliseconds ago. <see cref="ReceivedAtUtc"/>
/// is when this process had it in hand. They are different, and the gap between them is the
/// cheapest diagnostic in the whole system: normally 30 ms, and when it becomes 900 ms a gateway
/// is struggling or a link is saturated, and you know before an operator notices the screen
/// lagging.
/// </para>
/// <para>
/// Almost every quick implementation stores one column, and the one it stores is the receive
/// time — or worse, <c>DateTime.Now</c> at the moment of the INSERT, which is a third clock
/// nobody asked for. Dropping the source time does not just lose the diagnostic; it makes the
/// data <i>wrong</i>, because samples do not necessarily arrive in the order they happened. A
/// gateway that buffers through a dropped link delivers a burst afterwards, and a chart drawn in
/// arrival order is misleading in exactly the situation you are investigating.
/// </para>
/// <para>
/// <b>The machine's clock is usually not on NTP.</b> Keeping both times is what lets you detect
/// that: a device eleven minutes slow produces a constant, implausible <see cref="Age"/>, which
/// is a finding. Store one column and the bad clock is baked in silently.
/// </para>
/// Covered in: <c>course/module-28-industrial-and-ot/03-telemetry-and-backpressure.md</c>
/// </remarks>
/// <param name="EquipmentId">Which machine reported it.</param>
/// <param name="Tag">The signal name, in the gateway's vocabulary rather than the wire's.</param>
/// <param name="Value">The reading, already scaled into engineering units by the adapter.</param>
/// <param name="Quality">How much to believe it.</param>
/// <param name="SourceTimestampUtc">When the MACHINE says it was true.</param>
/// <param name="ReceivedAtUtc">When THIS PROCESS had it.</param>
public readonly record struct TelemetrySample(
    EquipmentId EquipmentId,
    string Tag,
    double Value,
    Quality Quality,
    DateTimeOffset SourceTimestampUtc,
    DateTimeOffset ReceivedAtUtc)
{
    /// <summary>
    /// How old the reading already was when it arrived. Alarm on this growing.
    /// </summary>
    /// <remarks>
    /// Can be negative, and that is a finding rather than a bug in this property: it means the
    /// device's clock is <i>ahead</i> of ours. Clamping it here would hide exactly the thing
    /// worth seeing, so it is reported as measured and left for the caller to judge.
    /// </remarks>
    public TimeSpan Age => ReceivedAtUtc - SourceTimestampUtc;

    /// <summary>True when the value may be used in an aggregate.</summary>
    /// <remarks>
    /// Deliberately excludes <see cref="Automation.Quality.Uncertain"/>. A value the device
    /// will not vouch for is not a value you may quietly average.
    /// </remarks>
    public bool IsTrustworthy => Quality == Quality.Good;
}
