using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Labs.Playground;

/// <summary>
/// Demos for the runtime behaviour that bites in production — culture, dates,
/// JSON, exceptions and reflection.
/// </summary>
public static partial class Program
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  culture — the bug that only happens on the customer machine
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Culture()
    {
        var italian = CultureInfo.GetCultureInfo("it-IT");
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var invariant = CultureInfo.InvariantCulture;

        Console.WriteLine($"   this process is running as : {CultureInfo.CurrentCulture.Name}\n");

        Console.WriteLine("── 1. Numbers: the decimal separator ────────────────────────\n");

        Console.WriteLine($"   (1234.5m).ToString(invariant) = \"{1234.5m.ToString(invariant)}\"");
        Console.WriteLine($"   (1234.5m).ToString(it-IT)     = \"{1234.5m.ToString(italian)}\"\n");

        Console.WriteLine("   Now parse the INVARIANT text with the ITALIAN culture:");
        TryParse("1234.5", italian);
        TryParse("1234.5", invariant);
        Console.WriteLine();
        Console.WriteLine("   In it-IT the dot is a THOUSANDS separator, so a price written by a");
        Console.WriteLine("   machine in invariant format is silently read back a thousand times too");
        Console.WriteLine("   large — or rejected. No exception, no log line, just a wrong number.\n");

        Console.WriteLine("── 2. Text: the Turkish I ───────────────────────────────────\n");

        Console.WriteLine($"   \"FILE\".ToLower(invariant) = \"{"FILE".ToLower(invariant)}\"");
        Console.WriteLine($"   \"FILE\".ToLower(tr-TR)     = \"{"FILE".ToLower(turkish)}\"   <- dotless i");
        Console.WriteLine($"   \"i\".ToUpper(tr-TR)        = \"{"i".ToUpper(turkish)}\"      <- dotted I\n");

        Console.WriteLine("   So the classic `s.ToLower() == \"file\"` check FAILS on a Turkish");
        Console.WriteLine("   machine, and the classic `ToUpper()`-based routing table breaks with it.");
        Console.WriteLine("   This is not hypothetical: it is the most famous localisation bug there is.\n");

        Console.WriteLine("── 3. Comparison: three different answers ───────────────────\n");

        const string a = "encyclopaedia";
        const string b = "encyclopædia";

        Console.WriteLine($"   Ordinal           : {string.Compare(a, b, StringComparison.Ordinal),3}   (byte values)");
        Console.WriteLine($"   InvariantCulture  : {string.Compare(a, b, StringComparison.InvariantCulture),3}   (linguistic)");
        Console.WriteLine($"   CurrentCulture    : {string.Compare(a, b, StringComparison.CurrentCulture),3}   (whatever the machine says)\n");

        Console.WriteLine("── The rules ────────────────────────────────────────────────\n");
        Console.WriteLine("   IDENTIFIERS  (SKUs, keys, file paths, protocol tokens, enum names):");
        Console.WriteLine("      StringComparison.Ordinal / OrdinalIgnoreCase. Always. It is also");
        Console.WriteLine("      the fastest, because it is a memory comparison.\n");
        Console.WriteLine("   PERSISTENCE  (config, JSON, SQL literals, CSS, URLs):");
        Console.WriteLine("      CultureInfo.InvariantCulture on every ToString and Parse.\n");
        Console.WriteLine("   HUMANS       (what a user reads or types, and sort order in a list):");
        Console.WriteLine("      CurrentCulture — deliberately, and only there.\n");
        Console.WriteLine("   The analyzers CA1305, CA1307, CA1310 flag every call that did not say");
        Console.WriteLine("   which one it meant. Turn them on and the whole class of bug disappears.");

        static void TryParse(string text, CultureInfo culture)
        {
            string label = string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;

            Console.WriteLine(decimal.TryParse(text, NumberStyles.Number, culture, out decimal value)
                ? $"      decimal.Parse(\"{text}\", {label,-9}) = {value,10}"
                : $"      decimal.Parse(\"{text}\", {label,-9}) = FAILED");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  datetime — Kind, offsets, DST, and TimeProvider
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void DateAndTime()
    {
        Console.WriteLine("── 1. DateTime carries a Kind, and it is a suggestion ───────\n");

        var unspecified = new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified);
        DateTime utc = DateTime.UtcNow;
        DateTime local = DateTime.Now;

        Console.WriteLine($"   new DateTime(...)  Kind = {unspecified.Kind}   <- the default, and it means 'nobody said'");
        Console.WriteLine($"   DateTime.UtcNow    Kind = {utc.Kind}");
        Console.WriteLine($"   DateTime.Now       Kind = {local.Kind}\n");

        Console.WriteLine("   Kind is NOT persisted by most databases. A DateTime that goes into SQL");
        Console.WriteLine("   Server as Utc comes back as Unspecified, and the next ToLocalTime()");
        Console.WriteLine("   shifts it by your offset. Round-trip a value twice and it moves twice.\n");

        Console.WriteLine("── 2. DateTimeOffset knows what DateTime is guessing ────────\n");

        var offset = new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.FromHours(2));
        Console.WriteLine($"   {offset:O}");
        Console.WriteLine($"   .UtcDateTime = {offset.UtcDateTime:O}\n");
        Console.WriteLine("   An offset is unambiguous, so it is the right type for an INSTANT:");
        Console.WriteLine("   when an order was placed, when a row was written, when a token expires.\n");

        Console.WriteLine("── 3. But an offset is still not a time zone ────────────────\n");

        try
        {
            TimeZoneInfo rome = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Rome");

            var springForward = new DateTime(2026, 3, 29, 2, 30, 0);
            Console.WriteLine($"   Is 2026-03-29 02:30 a valid local time in Rome? {!rome.IsInvalidTime(springForward)}");
            Console.WriteLine("   It is not — the clocks jump from 02:00 to 03:00 that night, so that");
            Console.WriteLine("   half hour does not exist. A nightly job scheduled at 02:30 does not");
            Console.WriteLine("   run once a year, and a job at 02:30 in October runs TWICE.\n");

            var autumnBack = new DateTime(2026, 10, 25, 2, 30, 0);
            Console.WriteLine($"   Is 2026-10-25 02:30 ambiguous in Rome?          {rome.IsAmbiguousTime(autumnBack)}\n");
        }
        catch (TimeZoneNotFoundException)
        {
            Console.WriteLine("   (no Rome time zone on this machine — skipping the DST demo)\n");
        }

        Console.WriteLine("── 4. DateOnly and TimeOnly ─────────────────────────────────\n");

        var shipDate = new DateOnly(2026, 7, 15);
        Console.WriteLine($"   DateOnly : {shipDate}   <- a birthday and a due date are not instants,");
        Console.WriteLine("              and modelling them as DateTime is what puts a 00:00 in the");
        Console.WriteLine("              database and a timezone bug in the report.\n");

        Console.WriteLine("── 5. TimeProvider: the reason your tests are flaky ─────────\n");

        Console.WriteLine("   DateTime.UtcNow is a static call to the operating system. Code that uses");
        Console.WriteLine("   it directly cannot be tested for 'what happens at month end', cannot be");
        Console.WriteLine("   tested for expiry, and cannot be tested for DST.\n");
        Console.WriteLine("   Inject TimeProvider (.NET 8+) instead:");
        Console.WriteLine("      production : TimeProvider.System");
        Console.WriteLine("      tests      : FakeTimeProvider, and you Advance() it by hand\n");
        Console.WriteLine($"   TimeProvider.System.GetUtcNow() = {TimeProvider.System.GetUtcNow():O}\n");

        Console.WriteLine("── The rules ────────────────────────────────────────────────\n");
        Console.WriteLine("   Store UTC. Send ISO-8601 with an offset (\"O\" format). Convert to a");
        Console.WriteLine("   time zone only at the edge, for display. Never subtract two local");
        Console.WriteLine("   DateTimes and call the result a duration.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  json — System.Text.Json, and the four traps
    // ═══════════════════════════════════════════════════════════════════════════════════

    private class Notification
    {
        public string Channel { get; set; } = "";
    }

    private sealed class EmailNotification : Notification
    {
        public string Address { get; set; } = "";
    }

    private sealed record OrderDto(string Number, decimal Total, OrderState State);

    private enum OrderState
    {
        Draft = 0,
        Submitted = 1,
    }

    private static void Json()
    {
        Console.WriteLine("── Trap 1: the DECLARED type decides what is serialized ─────\n");

        Notification n = new EmailNotification { Channel = "email", Address = "a@b.it" };

        Console.WriteLine($"   Serialize(n)                        -> {JsonSerializer.Serialize(n)}");
        Console.WriteLine($"   Serialize(n, n.GetType())           -> {JsonSerializer.Serialize(n, n.GetType())}\n");
        Console.WriteLine("   The first one silently dropped Address, because the variable is typed");
        Console.WriteLine("   as the base class. This is the number-one 'the field is missing in the");
        Console.WriteLine("   response' bug, and it never throws.\n");
        Console.WriteLine("   The fix in modern code is [JsonDerivedType] on the base, which also");
        Console.WriteLine("   writes a discriminator so the value round-trips.\n");

        Console.WriteLine("── Trap 2: casing is not symmetric by default ───────────────\n");

        var dto = new OrderDto("ORD-2026-000001", 149.90m, OrderState.Submitted);

        Console.WriteLine($"   default options : {JsonSerializer.Serialize(dto)}");
        Console.WriteLine($"   web defaults    : {JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web))}\n");
        Console.WriteLine("   The bare JsonSerializer keeps PascalCase; ASP.NET Core uses camelCase.");
        Console.WriteLine("   So a payload built in a background job and a payload built by a");
        Console.WriteLine("   controller disagree, and only the second one matches the front end.\n");
        Console.WriteLine("   Deserialization is case-INSENSITIVE under web defaults and case-");
        Console.WriteLine("   SENSITIVE by default, which is why the round trip appears to work");
        Console.WriteLine("   right up until someone reads it with the other options object.\n");

        Console.WriteLine("── Trap 3: enums travel as numbers ──────────────────────────\n");

        Console.WriteLine($"   as numbers : {JsonSerializer.Serialize(dto)}");

        var withStrings = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        Console.WriteLine($"   as strings : {JsonSerializer.Serialize(dto, withStrings)}\n");
        Console.WriteLine("   `1` means whatever the enum happens to mean today. Insert a member and");
        Console.WriteLine("   every stored message changes meaning — see module 01. Across a published");
        Console.WriteLine("   boundary, serialize enums as strings and pin the numeric values anyway.\n");

        Console.WriteLine("── Trap 4: deserializing to object gives you JsonElement ────\n");

        object parsed = JsonSerializer.Deserialize<object>("""{"total": 149.90}""")!;
        Console.WriteLine($"   typeof                 : {parsed.GetType().Name}");
        Console.WriteLine($"   is it a Dictionary?    : {parsed is Dictionary<string, object>}\n");
        Console.WriteLine("   And a JsonElement is a WINDOW over a buffer. Once the document is");
        Console.WriteLine("   disposed, reading it throws. Deserialize to a real type, or to");
        Console.WriteLine("   JsonDocument and clone what you need out of it.\n");

        Console.WriteLine("── And the one that is not a trap, it is just correct ───────\n");
        Console.WriteLine("   Source generation. [JsonSerializable] + a JsonSerializerContext means");
        Console.WriteLine("   the reader and writer are generated at COMPILE time: no reflection, no");
        Console.WriteLine("   startup cost, works under Native AOT and trimming. In .NET 10 this is");
        Console.WriteLine("   the default choice for a Web API, not an optimisation.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  exceptions — throw vs throw ex, filters, and what one costs
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Exceptions()
    {
        Console.WriteLine("── 1. `throw ex` destroys the evidence ──────────────────────\n");

        Console.WriteLine("   rethrown with `throw ex;`  ->");
        Console.WriteLine($"      {FirstLineOfStack(() => Rethrow(useThrowEx: true))}");
        Console.WriteLine("   rethrown with `throw;`     ->");
        Console.WriteLine($"      {FirstLineOfStack(() => Rethrow(useThrowEx: false))}\n");

        Console.WriteLine("   `throw ex` resets the stack trace to the line that rethrew it, so the");
        Console.WriteLine("   frame where the bug actually happened is gone. `throw;` preserves it.");
        Console.WriteLine("   To rethrow from somewhere else entirely (a callback, a continuation),");
        Console.WriteLine("   capture it: ExceptionDispatchInfo.Capture(ex).Throw().\n");

        Console.WriteLine("── 2. An exception filter runs BEFORE the stack unwinds ─────\n");

        try
        {
            Inner();
        }
        catch (InvalidOperationException) when (Log("      2. filter ran, up in the CALLER"))
        {
            Console.WriteLine("      4. catch body ran");
        }

        Console.WriteLine("\n   Read that order again: the caller's FILTER ran before the callee's");
        Console.WriteLine("   FINALLY. Exception handling has two passes. The first walks up the");
        Console.WriteLine("   stack asking every filter 'is this yours?', with every frame still");
        Console.WriteLine("   intact. Only once a handler says yes does the second pass unwind,");
        Console.WriteLine("   running the finally blocks on the way.\n");
        Console.WriteLine("   Two consequences worth having ready:");
        Console.WriteLine("      - `catch (X) when (...)` gives a crash dump with the ORIGINAL stack");
        Console.WriteLine("        intact, which catch-log-rethrow does not;");
        Console.WriteLine("      - a filter with a side effect runs at a moment nobody expects, and");
        Console.WriteLine("        runs even when the exception is ultimately handled elsewhere.\n");

        Console.WriteLine("── 3. What it costs ─────────────────────────────────────────\n");

        const int iterations = 10_000;

        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            try
            {
                throw new InvalidOperationException("expected");
            }
            catch (InvalidOperationException)
            {
            }
        }

        watch.Stop();
        long throwBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        long throwMs = watch.ElapsedMilliseconds;

        before = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            _ = TryIt(out _);
        }

        watch.Stop();
        long resultBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"   {iterations:N0} throw+catch : {throwMs,6:N0} ms   {throwBytes,10:N0} bytes");
        Console.WriteLine($"   {iterations:N0} bool return : {watch.ElapsedMilliseconds,6:N0} ms   {resultBytes,10:N0} bytes\n");

        Console.WriteLine("   (Indicative only — see labs/Labs.Benchmarks for the measured version.)\n");

        Console.WriteLine("   Two costs, and the second is the one people forget: capturing the stack");
        Console.WriteLine("   trace, and the two-pass unwind. A thrown-and-caught exception also");
        Console.WriteLine("   breaks the JIT out of any inlining around it.\n");

        Console.WriteLine("── The rule ─────────────────────────────────────────────────\n");
        Console.WriteLine("   Exceptions are for the EXCEPTIONAL: a bug, or infrastructure failing.");
        Console.WriteLine("   'That SKU does not exist' and 'the order was already submitted' are");
        Console.WriteLine("   ordinary outcomes of a working system, so they are Results. See");
        Console.WriteLine("   Domain/Results/Result.cs, and module 05.\n");
        Console.WriteLine("   Corollaries: never catch Exception except at the outermost boundary;");
        Console.WriteLine("   never use exceptions for control flow; never swallow one silently; and");
        Console.WriteLine("   an empty catch block is a decision to hide a bug from yourself.");

        static void Inner()
        {
            try
            {
                Console.WriteLine("      1. about to throw");
                throw new InvalidOperationException("boom");
            }
            finally
            {
                Console.WriteLine("      3. finally in the throwing method ran");
            }
        }

        static bool Log(string message)
        {
            Console.WriteLine(message);
            return true;
        }

        static bool TryIt(out int value)
        {
            value = 0;
            return false;
        }

        static void Rethrow(bool useThrowEx)
        {
            try
            {
                Deep();
            }
            catch (InvalidOperationException ex)
            {
                if (useThrowEx)
                {
#pragma warning disable CA2200 // destroying the stack trace is exactly what this demo is about
                    throw ex;       // resets the stack trace to HERE
#pragma warning restore CA2200
                }

                throw;              // preserves it
            }

            static void Deep() => throw new InvalidOperationException("the original problem");
        }

        static string FirstLineOfStack(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                string? line = ex.StackTrace?
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?
                    .Trim();

                return line ?? "(no stack trace)";
            }

            return "(did not throw)";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  reflection — and the cached delegate that makes it disappear
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class Row
    {
        public string Sku { get; set; } = "ELE-100001";

        public int Quantity { get; set; } = 5;
    }

    private static void Reflection()
    {
        var row = new Row();
        const int iterations = 1_000_000;

        PropertyInfo property = typeof(Row).GetProperty(nameof(Row.Sku))!;
        Func<Row, string> compiled = (Func<Row, string>)Delegate.CreateDelegate(
            typeof(Func<Row, string>), property.GetGetMethod()!);

        // Every loop accumulates into `sink` so the JIT cannot delete the work,
        // and every path is warmed first so we are not timing the JIT itself.
        long sink = 0;

        for (int i = 0; i < 2000; i++)
        {
            sink += row.Sku.Length;
            sink += ((string)typeof(Row).GetProperty(nameof(Row.Sku))!.GetValue(row)!).Length;
            sink += ((string)property.GetValue(row)!).Length;
            sink += compiled(row).Length;
        }

        var watch = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            sink += row.Sku.Length;
        }

        long direct = watch.ElapsedMilliseconds;

        watch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            sink += ((string)typeof(Row).GetProperty(nameof(Row.Sku))!.GetValue(row)!).Length;
        }

        long lookupEachTime = watch.ElapsedMilliseconds;

        watch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            sink += ((string)property.GetValue(row)!).Length;
        }

        long cachedPropertyInfo = watch.ElapsedMilliseconds;

        watch.Restart();
        for (int i = 0; i < iterations; i++)
        {
            sink += compiled(row).Length;
        }

        long cachedDelegate = watch.ElapsedMilliseconds;

        Console.WriteLine($"   COST 1 — the metadata lookup. {iterations:N0} reads (sink = {sink:N0}):\n");
        Console.WriteLine($"      row.Sku                          : {direct,6:N0} ms   <- the baseline");
        Console.WriteLine($"      GetProperty(...).GetValue(row)   : {lookupEachTime,6:N0} ms   <- looks the name up EVERY time");
        Console.WriteLine($"      cached PropertyInfo.GetValue(row): {cachedPropertyInfo,6:N0} ms   <- lookup done once");
        Console.WriteLine($"      cached delegate                  : {cachedDelegate,6:N0} ms   <- a direct call, near enough\n");

        Console.WriteLine("   (Console timings wobble — the ORDER is the lesson, not the numbers.");
        Console.WriteLine("    Use BenchmarkDotNet when a number has to be defensible. Module 14.)\n");

        PropertyInfo quantity = typeof(Row).GetProperty(nameof(Row.Quantity))!;

        Console.WriteLine("   COST 2 — the boxing, which IS exact and IS reproducible:\n");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            sink += row.Quantity;
        }

        long directBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            sink += (int)quantity.GetValue(row)!;
        }

        long reflectedBytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"      100,000 x row.Quantity                : {directBytes,10:N0} bytes");
        Console.WriteLine($"      100,000 x PropertyInfo.GetValue(row)  : {reflectedBytes,10:N0} bytes\n");
        Console.WriteLine("      GetValue returns `object`, so every int read is a heap allocation.");
        Console.WriteLine("      Do that per row per column while mapping a result set and you have");
        Console.WriteLine("      invented a gen-0 collection per page of data.\n");

        Console.WriteLine("   Reflection is not slow because it is reflection. It is slow because of");
        Console.WriteLine("   the METADATA LOOKUP and the boxing. Do the lookup once, build a");
        Console.WriteLine("   delegate, cache it in a static Dictionary<Type, ...> — and the cost");
        Console.WriteLine("   is gone. That is exactly what a mediator, an ORM and a serializer do.\n");

        Console.WriteLine("   Better still: do not do it at run time at all. A SOURCE GENERATOR does");
        Console.WriteLine("   the same work at compile time, produces readable C#, and survives");
        Console.WriteLine("   trimming and Native AOT — where reflection over types nobody references");
        Console.WriteLine("   statically simply throws at run time, because the type was trimmed away.\n");

        Console.WriteLine("   See Dispatcher.cs: the pipeline is resolved once and closed over, so");
        Console.WriteLine("   the per-request path has no reflection in it at all.");
    }
}
