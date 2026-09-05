# Module 23 — Text, culture, time, and serialization

> Four topics that share one property: they are invisible on your machine and wrong on someone
> else's. Every bug in this module ships green, passes review, and appears the first time the code
> runs in a different country, a different time zone, or against a different serializer
> configuration. That makes them disproportionately valuable to know cold — and if you are
> interviewing in Italy, the culture section is not academic. It is Tuesday.

```bash
cd labs/Labs.Playground
dotnet run culture     # the Turkish I, and 1.5 parsed as 15
dotnet run datetime    # Kind, offsets, DST, TimeProvider
dotnet run json        # System.Text.Json, four traps
dotnet run strings     # interning, ==, and the O(n^2) concatenation
```

---

## 1. What a string actually is

A `string` is an **immutable, UTF-16, length-prefixed** sequence of `char`. Three consequences:

**Immutable** — every "modification" allocates a new one. `s += "x"` in a 1,000-iteration loop
allocates about 1 MB where a `StringBuilder` allocates about 8 KB. `dotnet run strings` measures
it. For three concatenations, `+` is clearer and fine; in a loop it is a bug.

**UTF-16** — a `char` is 16 bits, which is **not** a character. Anything outside the Basic
Multilingual Plane (emoji, some CJK, historic scripts) is a *surrogate pair*: two `char`s for one
symbol. So:

```csharp
"👍".Length          // 2   — not 1
"é".Length     // 2   — "é" written as e + combining accent
"é".Length           // 1   — the same visual character, precomposed
```

Which gives three rules: **never reverse a string by reversing `char`s**, never truncate to a
fixed `char` count and expect valid text out, and normalise (`string.Normalize()`) before
comparing text that came from different sources — a Mac and a Windows machine encode `é`
differently by default.

**Interned** — string *literals* are shared process-wide. `ReferenceEquals("hello", "hello")` is
`true`; the same value built at run time is a different object. This matters twice: it is why
`lock ("something")` is catastrophic (module 21), and it is why `==` on strings compares *value*,
not reference, so you rarely have to think about it.

---

## 2. Culture: the three-way rule

This is the section to memorise. Every `ToString`, `Parse`, `ToUpper`, `Compare` and `StartsWith`
makes a culture decision, and the default is almost always the wrong one.

```
   IDENTIFIERS          SKUs, keys, file paths, protocol tokens, enum names, HTTP headers
      → StringComparison.Ordinal / OrdinalIgnoreCase.  Always. Also the fastest.

   PERSISTENCE          config files, JSON, SQL literals, CSS, URLs, logs, CSV
      → CultureInfo.InvariantCulture on every ToString and Parse.

   HUMANS               what a user reads or types, and the sort order of a displayed list
      → CurrentCulture — deliberately, and only here.
```

**Why it is not pedantry.** On an Italian machine:

```csharp
1234.5m.ToString(CultureInfo.InvariantCulture)   // "1234.5"
1234.5m.ToString(italian)                        // "1234,5"

decimal.Parse("1234.5", italian)                 // 12345   ← the dot is a THOUSANDS separator
```

No exception. No log line. A price a thousand times too large, written by one machine and read by
another. `dotnet run culture` prints exactly that.

And in Turkish:

```csharp
"FILE".ToLower(turkish)   // "fıle"  — dotless i
"i".ToUpper(turkish)      // "İ"     — dotted capital
```

So `s.ToLower() == "file"` is false on a Turkish machine. This is the most famous localisation bug
there is, and it is still shipped every year.

**One extra place it bites, and it is the one that reaches the browser:** anything you format into
CSS, JSON or a URL. `$"width:{percent}%"` on an Italian machine produces `width:33,33%`, which the
browser silently drops. Module 18 has this one in the Blazor context; the fix is
`percent.ToString(CultureInfo.InvariantCulture)`.

**Turn the analyzers on and the whole class disappears:** CA1305 (`IFormatProvider`), CA1307
(`StringComparison` on equality), CA1310 (`StringComparison` on `StartsWith`/`EndsWith`/`IndexOf`),
CA1311 (`ToUpper`/`ToLower` without a culture). With warnings-as-errors in `src/`, an unspecified
comparison stops the build.

**Ordinal is also faster.** It is a memory comparison; a linguistic comparison walks ICU collation
tables. So for identifiers the correct answer and the fast answer are the same, which is a rare
gift.

---

## 3. Time

**`DateTime` carries a `Kind`, and the `Kind` is a suggestion.** `Unspecified`, `Utc`, `Local` —
and most databases do not persist it. A value written as `Utc` comes back `Unspecified`, and the
next `ToLocalTime()` shifts it again. Round-trip twice and it moves twice.

**Use the right type for the question:**

| Type | Means | Use for |
|---|---|---|
| `DateTimeOffset` | an unambiguous instant | when something happened; expiry; audit rows |
| `DateOnly` | a calendar day | a birthday, an invoice date, a delivery date |
| `TimeOnly` | a wall-clock time | opening hours, a cut-off |
| `TimeSpan` | a duration | elapsed time, a timeout |
| `DateTime` | a date and time of *unstated* zone | legacy, and interop you do not control |

Modelling a delivery date as `DateTime` is what puts a `00:00:00` in the database and a
timezone bug in the monthly report.

**An offset is still not a time zone.** `+02:00` tells you the offset *that day*; it does not tell
you what Rome will do in October. Only a `TimeZoneInfo` knows the rules:

```csharp
rome.IsInvalidTime(new DateTime(2026, 3, 29, 2, 30, 0))    // true — that half hour does not exist
rome.IsAmbiguousTime(new DateTime(2026, 10, 25, 2, 30, 0)) // true — it happens twice
```

`dotnet run datetime` prints both. The practical consequence: **a nightly job scheduled at 02:30
skips a night every March and runs twice every October.** Schedule maintenance jobs in UTC, or at
a time that exists on every day of the year.

**Store UTC, transmit ISO-8601 with an offset (the `"O"` format), convert at the edge for
display.** And never subtract two local `DateTime`s and call the result a duration — across a DST
boundary it is off by an hour.

**`TimeProvider` (.NET 8+) is why your tests are flaky.** `DateTime.UtcNow` is a static call to
the OS, so code that uses it cannot be tested for month-end, expiry or DST. Inject
`TimeProvider.System` in production and `FakeTimeProvider` in tests, and `Advance()` it by hand.
This is the same argument as every other injected dependency; time just took longer to be
recognised as one.

---

## 4. `System.Text.Json`

`dotnet run json` demonstrates all four of these. They are the ones that reach production.

**Trap 1 — the *declared* type decides what gets serialized.**

```csharp
Notification n = new EmailNotification { Channel = "email", Address = "a@b.it" };
JsonSerializer.Serialize(n);                 // {"Channel":"email"}     ← Address silently gone
JsonSerializer.Serialize(n, n.GetType());    // {"Address":"a@b.it","Channel":"email"}
```

This is the number-one "the field is missing from the response" bug, and it never throws. The
modern fix is `[JsonDerivedType]` on the base type, which also writes a discriminator so the value
round-trips.

**Trap 2 — casing is not symmetric.** Bare `JsonSerializer` keeps PascalCase and deserializes
case-*sensitively*; `JsonSerializerDefaults.Web` (what ASP.NET Core uses) writes camelCase and
reads case-*insensitively*. So a payload built by a background job and a payload built by an
endpoint disagree, and only one matches the front end. **Register one `JsonSerializerOptions`
instance and use it everywhere** — it is also a real performance point, because the options object
caches per-type metadata and constructing a new one per call throws that cache away.

**Trap 3 — enums travel as numbers by default.** `"state": 1` means whatever the enum means today;
insert a member without pinned values and every stored message changes meaning (module 01). Across
a published boundary: `JsonStringEnumConverter`, *and* pinned numeric values anyway.

**Trap 4 — deserializing to `object` gives you a `JsonElement`,** not a dictionary. And a
`JsonElement` is a window over a buffer: once the owning document is disposed, reading it throws.
Deserialize to a real type, or to `JsonDocument` and clone what you need out.

**And the thing that is simply correct: source generation.**

```csharp
[JsonSerializable(typeof(OrderDto))]
internal sealed partial class AppJsonContext : JsonSerializerContext;
```

The reader and writer are generated at compile time — no reflection, no startup cost, works under
trimming and Native AOT. In .NET 10 this is the default choice for a Web API, not an optimisation.

**Two more worth knowing.** `ReferenceHandler.IgnoreCycles` or a DTO fixes an infinite recursion
on a bidirectional navigation property — the second option is the right one, because serializing
an EF entity directly is the bug (module 06). And `required` members plus a constructor give you
deserialization that *fails* on missing data instead of silently producing `null` inside a
non-nullable property.

📂 [`Web/Contracts/OrderContracts.cs`](../../src/LogiFlow.Web/Contracts/OrderContracts.cs) — note
that the Blazor client **duplicates** the contract rather than sharing the Application layer's
types. Module 18 explains why: across a published boundary you want a rename to break the client.

---

## 5. Do this

```bash
cd labs/Labs.Playground
dotnet run culture
dotnet run datetime
dotnet run json
```

Then make it personal, which is the point of the module:

1. Run the API with an Italian culture and watch a decimal change shape:
   ```bash
   DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY=false dotnet run --project src/LogiFlow.Api
   ```
   In `Demos.Runtime.cs`, set `CultureInfo.CurrentCulture = new CultureInfo("it-IT")` at the top of
   `Culture()` and re-run. Every "on an Italian machine" line in this module becomes your machine.
2. Find one `ToString()` or `Parse` in `src/` with no `IFormatProvider` and decide which of the
   three categories it belongs to. (If the analyzers are doing their job, there is nothing to
   find — confirm that, and then you have learned something too.)
3. Serialize an `Order` entity directly with `JsonSerializer.Serialize` and watch it fail or
   recurse. Then serialize the DTO. That contrast is module 06's rule, made visible.

---

## 6. Golden rules

1. **`string` is immutable, so every concatenation allocates.** Fine for three; O(n²) in a loop.
   `StringBuilder`, or `string.Create`.
2. **A `char` is a UTF-16 code unit, not a character.** An emoji has `Length == 2`. Never reverse
   or truncate by `char` index, and normalise before comparing text from different sources.
3. **Ordinal for identifiers, Invariant for persistence, Current for humans.** Three categories,
   no fourth, and every call site belongs to exactly one.
4. **An unqualified `ToString()`/`Parse` is a latent bug on a non-English machine.** On `it-IT`,
   `"1234.5"` parses to `12345`, silently.
5. **`ToLower()` without a culture fails in Turkish.** The dotless `ı` breaks the oldest string
   comparison in the book. Use `OrdinalIgnoreCase` rather than case-folding at all.
6. **Anything that becomes CSS, JSON, a URL or SQL is formatted with `InvariantCulture`.**
   `width:33,33%` is silently dropped by every browser.
7. **Ordinal is both the correct answer and the fast one** for identifiers — it is a memory
   compare, not a collation walk.
8. **Store UTC, transmit ISO-8601 with an offset, convert only at the edge.**
9. **`DateTime.Kind` is not persisted by most databases**, so a Utc value comes back Unspecified
   and shifts on the next conversion. Use `DateTimeOffset` for an instant.
10. **An offset is not a time zone.** Only `TimeZoneInfo` knows that 02:30 does not exist one night
    in March and happens twice one night in October.
11. **Inject `TimeProvider`.** `DateTime.UtcNow` is a hidden static dependency, and it is why
    month-end and expiry logic cannot be tested.
12. **`JsonSerializer` serializes the DECLARED type.** A derived object assigned to a base-typed
    variable silently loses its extra properties. `[JsonDerivedType]`, or serialize
    `n.GetType()`.
13. **Register one `JsonSerializerOptions` and reuse it.** It caches per-type metadata; a new
    instance per call throws that away, and mismatched instances give you two casing conventions.
14. **Serialize enums as strings across a published boundary** — and pin the numeric values
    anyway.
15. **Deserializing to `object` yields a `JsonElement` over a buffer** that throws once the
    document is disposed.
16. **Use JSON source generation.** Compile-time, no reflection, trimming- and AOT-safe.

---

## 7. Interview questions

**"Why is `"👍".Length` 2?"**
Because `string` is UTF-16 and `Length` counts code units, not characters. Anything outside the
Basic Multilingual Plane is a surrogate pair. It is why reversing or truncating by `char` index
corrupts text.

**"When would you use `StringComparison.Ordinal` over `CurrentCulture`?"**
For anything that is an identifier rather than human language: SKUs, keys, file paths, header
names, enum names. It is culture-independent, so it behaves the same on every machine, and it is
faster because it compares memory rather than walking collation tables.

**"What is the Turkish I problem?"**
In Turkish, lowercase `I` is dotless `ı` and uppercase `i` is dotted `İ`. So a culture-sensitive
`ToLower()` comparison against `"file"` fails on a Turkish machine. The fix is to compare with
`OrdinalIgnoreCase` rather than case-folding first.

**"`DateTime` vs `DateTimeOffset` — which and why?"**
`DateTimeOffset` for an instant, because it is unambiguous and survives round-tripping;
`DateTime`'s `Kind` is not persisted by most databases. `DateOnly` for a calendar date. And
neither replaces `TimeZoneInfo`, because an offset does not know the DST rules.

**"How would you test code that expires a token after 30 minutes?"**
Inject `TimeProvider`, use `FakeTimeProvider` in the test, and `Advance(TimeSpan.FromMinutes(31))`.
With `DateTime.UtcNow` the only options are `Thread.Sleep` or not testing it.

**"Why did a property disappear from my JSON response?"**
The variable was typed as a base class, and `JsonSerializer` serializes the declared type. Use
`[JsonDerivedType]`, or pass the runtime type explicitly.

**"Why should `JsonSerializerOptions` be a singleton?"**
It caches the serialization metadata it builds per type. Creating a new one per call rebuilds that
cache every time, and using two different instances in one application gives you two casing
conventions that only disagree at the boundary.

---

## Next

→ [Module 24 — Security for a .NET API](../module-24-security/)
