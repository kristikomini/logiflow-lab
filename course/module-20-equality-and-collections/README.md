# Module 20 — Equality, hashing, and choosing a collection

> Two topics that look like trivia and are not. Equality is the contract every dictionary, every
> `Distinct()`, every `Contains` and every EF Core change-tracker silently depends on. Collections
> are the one place where picking the wrong type turns an O(n) loop into an O(n²) one, in code
> that looks identical.

```bash
cd labs/Labs.Playground
dotnet run equality    # the five kinds, and when each one fires
dotnet run hashcode    # the contract, and the mutable-key trap
```

---

## 1. Five kinds of equality, and which one your code just used

```
   ReferenceEquals(a, b)      the same object.            Never overridable. Always the truth.
   a == b                     STATIC. Compile-time type decides which operator runs.
   a.Equals(b)                VIRTUAL. Run-time type decides. object.Equals is reference equality.
   IEquatable<T>.Equals(b)    strongly typed, no boxing. What collections look for first.
   IEqualityComparer<T>       equality supplied from OUTSIDE, per call site.
```

The one that catches people:

```csharp
object a = new Money(10m, "EUR");
object b = new Money(10m, "EUR");

a == b          // False — object's ==, chosen at COMPILE time
a.Equals(b)     // True  — virtual, so the record's Equals runs
```

`==` is resolved by the compiler from the *declared* type. `Equals` is dispatched at run time
from the *actual* type. That difference is why:

> **Inside `bool Same<T>(T x, T y) => x == y;` with an unconstrained `T`, you get reference
> equality — even when `T` is a record.** The compiler had no operator to bind to. Use
> `EqualityComparer<T>.Default.Equals(x, y)`.

📂 [`Domain/Results/Error.cs`](../../src/LogiFlow.Domain/Results/Error.cs) — `Error` is a record,
so two errors with the same code are equal, which is what makes `result.Error == OrderErrors.NotFound`
read the way you want it to.

**Records get value equality for free, and one extra clause you should know about.** The
generated `Equals` begins with an `EqualityContract` type check, so a derived record is never
equal to its base even with identical fields. `ValidationError` never equals a plain `Error` with
the same code. Run `dotnet run equality` to watch it.

**Structs get value equality for free too — and it may be slow.** `ValueType.Equals` uses a fast
bitwise comparison only when the struct is "blittable" (no reference fields, no padding).
Otherwise it falls back to a field-by-field comparison driven by reflection, *and* it boxes both
sides because the signature takes `object`. The demo measures it: 10,000 comparisons, 480,000
bytes allocated. The fix is one word — make it a `readonly record struct`, which generates
`IEquatable<T>` and allocates nothing.

---

## 2. The `GetHashCode` contract

Three rules. Everyone knows the first, almost everyone breaks the third.

1. **Equal objects must return the same hash code.** This is what makes lookup correct.
2. **Unequal objects may return the same hash code.** This is what makes hashing possible at all
   — you are mapping infinite values onto 2³² buckets.
3. **The hash must not change while the object is in a hash table.**

Breaking rule 3 produces the strangest bug in the language:

```csharp
var key = new MutableKey { Value = "ELE-100001" };
var dict = new Dictionary<MutableKey, string> { [key] = "Wireless Scanner" };

dict.ContainsKey(key);   // True
key.Value = "ELE-999999";
dict.ContainsKey(key);   // False — the SAME object
dict.Count;              // 1 — and it is still in there
```

The entry sits in the bucket for the old hash; lookup goes to the bucket for the new one. The
value is now unreachable *and* unremovable. `dotnet run hashcode` prints exactly this.

> **Therefore: dictionary keys are immutable.** A `readonly record struct` id, a string, an enum.
> Never an entity, never anything with a setter.

**Writing one.** One line, always:

```csharp
public override int GetHashCode() => HashCode.Combine(Sku, WarehouseId);
```

Never XOR fields by hand — `a ^ b == b ^ a`, so `(1,2)` and `(2,1)` collide, and in a composite
key that is not a rare accident, it is half your data. Never sum them, for the same reason.
`HashCode.Combine` is seeded, order-sensitive, and free.

**And the one that surprises everybody:** `"abc".GetHashCode()` returns a *different number every
time the process starts*. .NET randomises the string hash seed per process to defend against
hash-flooding denial of service. So a hash code is valid for one process, for one run:

> **Never persist a `GetHashCode()` value, never send it over the wire, never shard on it.**
> When a hash has to survive, use SHA-256 and mean it.

---

## 3. Ordering: `IComparable<T>` vs `IComparer<T>`

Equality answers *"the same?"*. Ordering answers *"which comes first?"*, and they are separate
contracts that must not disagree:

| | Where it lives | When to use it |
|---|---|---|
| `IComparable<T>` | on the type | there is one obvious natural order (a date, a version) |
| `IComparer<T>` | in its own class | the order is a *policy* — by name, by revenue, by tier |

The rule the framework relies on: **if `CompareTo` returns 0, `Equals` should return true.**
`SortedDictionary` and `List.BinarySearch` use only `CompareTo`; `Dictionary` and `Distinct` use
only `Equals`/`GetHashCode`. Let them disagree and the same two objects are "the same" in one
collection and different in another — which is exactly as fun to debug as it sounds.

**A comparison must also be consistent**, or `List.Sort` will throw `InvalidOperationException:
IComparer.Compare() method returns inconsistent results`. The usual cause is comparing on a
mutable field, or a `Compare` that returns `x.Value - y.Value` on `int`s — which **overflows** for
large values and silently reverses the sign. Use `x.Value.CompareTo(y.Value)`.

---

## 4. Inside `Dictionary<K,V>`

Worth knowing because it explains every performance surprise a dictionary can produce.

```
   buckets: int[]          entries: Entry[]
   ┌────┐                  ┌──────────────────────────────────┐
   │  0 │──── empty        │ 0 │ hash │ next │ key │ value    │
   │  3 │──────────────────┤ 1 │ ...                          │
   │  0 │                  │ 2 │ ...                          │
   │  1 │────────┐         │ 3 │ hash │ -1   │ key │ value    │
   └────┘        └────────►└──────────────────────────────────┘

   Lookup:  hash = comparer.GetHashCode(key)
            bucket = hash % buckets.Length
            walk the chain, comparing FULL HASH first, then Equals
```

What falls out of that picture:

- **O(1) is an average, not a promise.** All keys colliding gives you O(n) — a linked list with
  extra steps. That is what a hash-flooding attack is, and why the string seed is randomised.
- **The full hash is compared before `Equals`.** So a cheap, well-distributed `GetHashCode` saves
  you from an expensive `Equals` almost every time.
- **Growth rehashes everything.** Adding *n* items with no capacity does ~log₂(n) full rehashes.
  `new Dictionary<K,V>(expectedCount)` when you know the size is the cheapest optimisation in the
  language.
- **Enumeration order is undefined.** It happens to be insertion order until the first removal,
  and code that depends on that will break in production and not in your test.
- **`TryGetValue` is one lookup. `ContainsKey` + indexer is two.** So is `if (!dict.ContainsKey(k))
  dict.Add(k, v)`, which should be `dict.TryAdd(k, v)`.

**A struct key with the default comparer boxes on every lookup** unless it implements
`IEquatable<T>`. This is the single most common accidental allocation in otherwise careful code.

---

## 5. Choosing a collection

| Need | Use | Lookup | Add | Notes |
|---|---|---|---|---|
| Ordered, indexed, grows | `List<T>` | O(n) | amortised O(1) | the default, and usually right |
| Fixed size, fastest | `T[]` | O(n) | — | covariance makes every store type-checked |
| Key → value | `Dictionary<K,V>` | O(1) avg | O(1) avg | undefined order |
| Membership only | `HashSet<T>` | O(1) avg | O(1) avg | `Contains` in a loop → use this |
| Sorted by key | `SortedDictionary<K,V>` | O(log n) | O(log n) | a tree; stable order |
| Sorted, read-mostly | `SortedList<K,V>` | O(log n) | O(n) | array-backed; less memory, slow insert |
| FIFO / LIFO | `Queue<T>` / `Stack<T>` | — | O(1) | intent is the point |
| Priority order | `PriorityQueue<T,P>` | — | O(log n) | .NET 6+; **not** a stable sort |
| Never changes after build | `FrozenDictionary` / `FrozenSet` | fastest | build-time | .NET 8+; slow to build, fastest to read |
| Cannot change, cheap to pass | `ImmutableArray<T>` | O(n) | O(n) copy | a struct wrapper over an array |
| Shared across threads | `ConcurrentDictionary<K,V>` | O(1) avg | O(1) avg | see the warning below |
| Producer/consumer | `Channel<T>` | — | — | module 04 |

**The two decisions that actually show up in a review:**

> **`Contains` inside a loop over a `List<T>` is O(n²).** `foreach (var x in a) if (b.Contains(x))`
> over two 10,000-item lists is 100 million comparisons. `b.ToHashSet()` first makes it 20,000.
> This is the most common real performance bug in business code, and it is invisible until the
> data grows.

> **`FrozenDictionary` for a lookup table built once at startup and read forever.** Currency
> codes, status maps, feature flags. It costs more to build and is meaningfully faster to read.
> 📂 See module 14, where this repository uses it.

**`ConcurrentDictionary` is not a magic thread-safe dictionary.** Each *operation* is atomic; a
sequence of them is not. `GetOrAdd` may run your factory **more than once** under contention
(only one result wins), so the factory must be cheap and side-effect free — if it opens a
connection, you just opened several. And `Count` takes every internal lock, so calling it in a
hot path is worse than the loop you were avoiding.

---

## 6. Do this

```bash
cd labs/Labs.Playground
dotnet run equality
dotnet run hashcode
```

Then the exercises in [`labs/Labs.Exercises/Exercises/Lab04_Equality.cs`](../../labs/Labs.Exercises/Exercises/Lab04_Equality.cs):

```bash
dotnet test labs/Labs.Exercises --filter "FullyQualifiedName~Lab04"
```

They are red. You will implement a correct `Equals`/`GetHashCode` pair, a case-insensitive
comparer, and a comparison that does not overflow — and one test proves the mutable-key bug
above, so you have to reason about *why* it is unfixable rather than patch it.

---

## 7. Golden rules

1. **`==` is static, `Equals` is virtual.** The compiler picks `==` from the declared type; the
   runtime picks `Equals` from the actual one. In generic code with an unconstrained `T`, `==` is
   reference equality — use `EqualityComparer<T>.Default`.
2. **Override `Equals` and you must override `GetHashCode`.** Equal objects must hash equally, or
   every hash-based collection quietly loses your data.
3. **The hash of a key must never change while it is in the table.** Mutate it and the entry
   becomes unreachable and unremovable — so dictionary keys are immutable, full stop.
4. **`HashCode.Combine`, never XOR and never sum.** XOR is commutative, so `(1,2)` and `(2,1)`
   collide, and in a composite key that is half your rows in one bucket.
5. **A `GetHashCode()` value is valid for one process, for one run.** The string seed is
   randomised per process. Never persist it, send it, or shard on it.
6. **A struct used as a dictionary key must implement `IEquatable<T>`** — otherwise every lookup
   boxes and falls back to a reflection-driven comparison. `readonly record struct` gives you
   both for free.
7. **If `CompareTo` returns 0, `Equals` must return true.** Sorted collections use only the
   first, hashed collections only the second; letting them disagree gives you two truths.
8. **Never write `a.Value - b.Value` in a comparer.** It overflows and reverses the sign.
   `CompareTo`, always.
9. **`Contains` in a loop over a `List<T>` is O(n²).** Build a `HashSet<T>` first. This is the
   most common real performance bug in ordinary business code.
10. **Size a dictionary you are about to fill.** Growth rehashes every entry, and the constructor
    takes the capacity.
11. **`TryGetValue` and `TryAdd` are one lookup; `ContainsKey` plus an indexer is two.**
12. **Dictionary enumeration order is undefined.** It looks like insertion order until the first
    removal, and then it does not.
13. **`ConcurrentDictionary` makes each operation atomic, not each sequence** — and `GetOrAdd`
    may invoke your factory more than once, so the factory must be cheap and side-effect free.

---

## 8. Interview questions

**"What is the contract between `Equals` and `GetHashCode`?"**
Equal objects must return equal hash codes; unequal objects may collide; and the hash must not
change while the object is a key. The first makes lookup correct, the second makes hashing
possible, the third is the one people break — and breaking it makes an entry unreachable and
unremovable.

**"`==` vs `Equals`?"**
`==` is a static operator resolved at compile time from the declared type; `Equals` is virtual and
dispatched at run time from the actual type. Cast a record to `object` and `==` becomes reference
equality while `Equals` still compares values.

**"Why does my struct allocate when I use it as a dictionary key?"**
Because it does not implement `IEquatable<T>`, so the default comparer falls back to
`ValueType.Equals(object)` — which boxes both operands and, for a non-blittable struct, compares
fields by reflection. Make it a `readonly record struct`.

**"How is `Dictionary<K,V>` implemented, and when is it not O(1)?"**
Buckets of chained entries indexed by `hash % length`, comparing the stored full hash before
calling `Equals`. It degrades to O(n) when hash codes collide — which is what a hash-flooding
attack causes, and why .NET randomises the string hash seed per process.

**"When would you use `HashSet` over `List`?"**
Whenever the question is membership rather than order. `Contains` is O(1) instead of O(n), which
turns the very common nested-loop lookup from O(n²) into O(n).

**"Is `ConcurrentDictionary` enough to make my cache thread-safe?"**
It makes each operation atomic, not each sequence. `GetOrAdd` can run the value factory more than
once under contention, so the factory must be side-effect free; if creating the value is
expensive or has side effects, store a `Lazy<T>` as the value instead.

---

## Next

→ [Module 21 — Threading and the memory model](../module-21-threading-and-memory-model/)
