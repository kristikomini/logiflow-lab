using System.Linq.Expressions;

namespace Labs.Playground;

/// <summary>
/// Demos for the language itself — equality, hashing, numbers, variance, generics,
/// iterators and expression trees.
/// </summary>
public static partial class Program
{
    // ═══════════════════════════════════════════════════════════════════════════════════
    //  equality — the five kinds, and when each one fires
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class PlainCustomer(string name)
    {
        public string Name { get; } = name;
    }

    private record Money2(decimal Amount, string Currency);

    private sealed record DiscountedMoney(decimal Amount, string Currency, decimal Discount)
        : Money2(Amount, Currency);

    private struct PlainPoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    private static void Equality()
    {
        Console.WriteLine("1. class — identity. Two objects with the same contents are NOT equal.\n");

        var c1 = new PlainCustomer("Rossi");
        var c2 = new PlainCustomer("Rossi");
        Console.WriteLine($"   c1 == c2        : {c1 == c2}");
        Console.WriteLine($"   c1.Equals(c2)   : {c1.Equals(c2)}");
        Console.WriteLine("   Because object.Equals is reference equality unless you override it.\n");

        Console.WriteLine("2. record — value. The compiler wrote Equals and GetHashCode for you.\n");

        var m1 = new Money2(10m, "EUR");
        var m2 = new Money2(10m, "EUR");
        Console.WriteLine($"   m1 == m2        : {m1 == m2}");
        Console.WriteLine($"   same hash       : {m1.GetHashCode() == m2.GetHashCode()}\n");

        Console.WriteLine("3. The EqualityContract clause — a derived record is never equal to its base.\n");

        Money2 baseMoney = new(10m, "EUR");
        Money2 derived = new DiscountedMoney(10m, "EUR", 0m);
        Console.WriteLine($"   baseMoney.Equals(derived) : {baseMoney.Equals(derived)}   <- same Amount, same Currency");
        Console.WriteLine("   A generated record Equals starts with an EqualityContract type check.");
        Console.WriteLine("   That is why ValidationError never equals a plain Error with the same code.\n");

        Console.WriteLine("4. The == operator is STATIC — it binds to the compile-time type. Equals does not.\n");

        object o1 = new Money2(10m, "EUR");
        object o2 = new Money2(10m, "EUR");
        Console.WriteLine($"   (object)m1 == (object)m2 : {o1 == o2}   <- object's ==, i.e. reference equality");
        Console.WriteLine($"   o1.Equals(o2)            : {o1.Equals(o2)}   <- virtual, so the record Equals runs");
        Console.WriteLine("   THE RULE: == is resolved at compile time, Equals at run time.");
        Console.WriteLine("   Inside a generic method with an unconstrained T, `a == b` is reference");
        Console.WriteLine("   equality even when T is a record. Use EqualityComparer<T>.Default.\n");

        Console.WriteLine("5. struct — the default is a field-by-field comparison, and it is slow.\n");

        var p1 = new PlainPoint(1, 2);
        var p2 = new PlainPoint(1, 2);
        Console.WriteLine($"   p1.Equals(p2)   : {p1.Equals(p2)}   (correct...)");
        Console.WriteLine("   ...but ValueType.Equals falls back to REFLECTION over the fields when the");
        Console.WriteLine("   struct contains any reference type or padding. That is why a struct used");
        Console.WriteLine("   as a dictionary key should implement IEquatable<T> — or just be a");
        Console.WriteLine("   `readonly record struct`, which generates it.\n");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = p1.Equals((object)p2);   // boxes both sides
        }

        long boxed = GC.GetAllocatedBytesForCurrentThread() - before;
        Console.WriteLine($"   10,000 boxed struct comparisons allocated {boxed:N0} bytes.");
        Console.WriteLine("   A `readonly record struct` does the same 10,000 comparisons for 0.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  hashcode — the contract, and the mutable-key trap
    // ═══════════════════════════════════════════════════════════════════════════════════

    private sealed class MutableKey
    {
        public string Value { get; set; } = "";

        public override bool Equals(object? obj) =>
            obj is MutableKey other && other.Value == Value;

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);
    }

    private static void HashCodes()
    {
        Console.WriteLine("THE CONTRACT, in three lines:\n");
        Console.WriteLine("  1. Equal objects MUST return the same hash code.");
        Console.WriteLine("  2. Unequal objects MAY return the same hash code (that is a collision).");
        Console.WriteLine("  3. The hash of an object must not change while it is in a hash table.\n");
        Console.WriteLine("Rule 1 is what makes lookup correct. Rule 2 is what makes it possible.");
        Console.WriteLine("Rule 3 is the one people break.\n");

        Console.WriteLine("── Breaking rule 3 ──────────────────────────────────────────\n");

        var key = new MutableKey { Value = "ELE-100001" };
        var dictionary = new Dictionary<MutableKey, string> { [key] = "Wireless Scanner" };

        Console.WriteLine($"   dictionary.ContainsKey(key) : {dictionary.ContainsKey(key)}");

        key.Value = "ELE-999999";   // the object is IN the dictionary, and its hash just changed

        Console.WriteLine("   ...mutate key.Value...");
        Console.WriteLine($"   dictionary.ContainsKey(key) : {dictionary.ContainsKey(key)}   <- the SAME object");
        Console.WriteLine($"   dictionary.Count            : {dictionary.Count}   <- and it is still in there\n");
        Console.WriteLine("   The entry sits in the bucket for the OLD hash; lookup goes to the new one.");
        Console.WriteLine("   It is unreachable and unremovable. This is why dictionary keys should be");
        Console.WriteLine("   immutable — a `readonly record struct` id, or a string.\n");

        Console.WriteLine("── Writing one properly ─────────────────────────────────────\n");
        Console.WriteLine("   public override int GetHashCode() => HashCode.Combine(Sku, WarehouseId);\n");
        Console.WriteLine("   Never XOR fields by hand: a ^ b == b ^ a, so (1,2) and (2,1) collide.");
        Console.WriteLine("   Never sum them, for the same reason. HashCode.Combine is seeded and");
        Console.WriteLine("   order-sensitive, and it is one line.\n");

        Console.WriteLine("── The one that surprises people ────────────────────────────\n");
        Console.WriteLine($"   \"ELE-100001\".GetHashCode() = {"ELE-100001".GetHashCode()}");
        Console.WriteLine("   Run this demo again. The number will be DIFFERENT.\n");
        Console.WriteLine("   .NET randomises the string hash seed per process, to defend against");
        Console.WriteLine("   hash-flooding attacks. So: never persist a GetHashCode() value, never");
        Console.WriteLine("   send it over the wire, never shard on it. It is valid for one process,");
        Console.WriteLine("   for one run. Use a real hash (SHA256) when the value has to survive.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  numbers — float, double, decimal, overflow
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Numbers()
    {
        Console.WriteLine("1. Binary floating point cannot represent 0.1.\n");

        double d = 0.1 + 0.2;
        Console.WriteLine($"   0.1 + 0.2        = {d:R}");
        Console.WriteLine($"   == 0.3           ? {d == 0.3}");
        Console.WriteLine($"   decimal version  = {0.1m + 0.2m}  == 0.3m ? {0.1m + 0.2m == 0.3m}\n");
        Console.WriteLine("   double is base-2. 0.1 is a repeating fraction in base 2, exactly as 1/3");
        Console.WriteLine("   is in base 10. decimal is base-10, so it stores 0.1 exactly.\n");

        Console.WriteLine("2. This is why money is decimal.\n");

        double cents = 0;
        for (int i = 0; i < 10; i++)
        {
            cents += 0.1;
        }

        decimal exact = 0;
        for (int i = 0; i < 10; i++)
        {
            exact += 0.1m;
        }

        Console.WriteLine($"   ten times 0.1 as double  : {cents:R}   == 1.0 ? {cents == 1.0}");
        Console.WriteLine($"   ten times 0.1 as decimal : {exact}     == 1.0m ? {exact == 1.0m}");
        Console.WriteLine("   Ten additions is enough to be wrong. An invoice has more than ten.\n");

        Console.WriteLine("3. Overflow is SILENT by default.\n");

        int max = int.MaxValue;
        unchecked
        {
            Console.WriteLine($"   unchecked: int.MaxValue + 1 = {max + 1}   <- wrapped to negative");
        }

        try
        {
            checked
            {
                Console.WriteLine($"   checked:   int.MaxValue + 1 = {max + 1}");
            }
        }
        catch (OverflowException)
        {
            Console.WriteLine("   checked:   OverflowException  <- what you actually wanted");
        }

        Console.WriteLine("\n   Set <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow> in the");
        Console.WriteLine("   .csproj and the whole assembly becomes checked. The cost is roughly one");
        Console.WriteLine("   branch per arithmetic operation. For business code that is free.\n");

        Console.WriteLine("4. Integer division truncates, and int/int stays int.\n");
        int a = 7, b = 2;
        Console.WriteLine($"   7 / 2            = {a / b}      <- not 3.5");
        Console.WriteLine($"   (double)7 / 2    = {(double)a / b}");
        Console.WriteLine($"   -7 / 2           = {-7 / 2}     <- truncates toward zero, not down");
        Console.WriteLine($"   -7 % 2           = {-7 % 2}     <- so the remainder can be negative\n");

        Console.WriteLine("5. Math.Round is banker's rounding by default.\n");
        Console.WriteLine($"   Math.Round(2.5)  = {Math.Round(2.5)}    <- to even, not up");
        Console.WriteLine($"   Math.Round(3.5)  = {Math.Round(3.5)}");
        Console.WriteLine($"   AwayFromZero     = {Math.Round(2.5, MidpointRounding.AwayFromZero)}");
        Console.WriteLine("   Invoicing rules generally expect arithmetic (away-from-zero) rounding on");
        Console.WriteLine("   the VAT line. The default will pass every unit test you thought to");
        Console.WriteLine("   write and lose a cent per invoice in production.\n");

        Console.WriteLine("6. NaN is not equal to itself.\n");
        double nan = double.NaN;
#pragma warning disable CS1718 // comparing a variable with itself: that IS the demo
        Console.WriteLine($"   nan == nan            : {nan == nan}");
#pragma warning restore CS1718
        Console.WriteLine($"   nan.Equals(nan)       : {nan.Equals(nan)}   <- Equals says yes!");
        Console.WriteLine("   The IEEE rule and the .NET collection rule disagree on purpose: a");
        Console.WriteLine("   List<double> containing NaN must still be able to find it again.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  variance — arrays lie, generics do not
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Variance()
    {
        Console.WriteLine("Array covariance was a C# 1.0 mistake, kept for compatibility.\n");

        string[] strings = ["a", "b"];
        object[] objects = strings;          // legal, and a lie: it is still a string[]

        Console.WriteLine("   string[] strings = [\"a\", \"b\"];");
        Console.WriteLine("   object[] objects = strings;     // compiles fine\n");

        try
        {
            objects[0] = 42;                  // the runtime checks every array store
        }
        catch (ArrayTypeMismatchException)
        {
            Console.WriteLine("   objects[0] = 42;  ->  ArrayTypeMismatchException at RUN TIME\n");
        }

        Console.WriteLine("   The consequence you pay for even when you never do this: the CLR must");
        Console.WriteLine("   type-check EVERY store into a reference-type array, because it cannot");
        Console.WriteLine("   know statically that the array is really what its type says.\n");

        Console.WriteLine("Generic variance is checked by the compiler instead.\n");

        IEnumerable<string> safeIn = strings;              // IEnumerable<out T> — covariant
        Console.WriteLine($"   IEnumerable<out T> : legal and safe — T only ever comes OUT ({safeIn.Count()} items)");
        Console.WriteLine("   List<T>            : ILLEGAL — a List<string> is not a List<object>,");
        Console.WriteLine("                        precisely because you could Add an int to it.\n");

        Action<object> printAnything = o => Console.WriteLine($"      got: {o}");
        Action<string> printString = printAnything;        // Action<in T> — contravariant
        printString("contravariance: an Action<object> can stand in for an Action<string>");

        Console.WriteLine("\n   THE MEMORY HOOK: `out` means it only ever comes out (covariant, like");
        Console.WriteLine("   IEnumerable). `in` means it only ever goes in (contravariant, like");
        Console.WriteLine("   Action and IComparer). Anything that does both is invariant.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  generics — what the JIT actually does with T
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static class Counter<T>
    {
        public static int Instances;
    }

    private interface IShout
    {
        string Shout();
    }

    private struct ShoutingStruct : IShout
    {
        public readonly string Shout() => "hi";
    }

    private static void CallViaInterface(IShout s) => _ = s.Shout();

    private static void CallViaConstraint<T>(T s)
        where T : IShout => _ = s.Shout();

    private static void Generics()
    {
        Console.WriteLine("1. A static field in a generic type is per CLOSED type, not per generic type.\n");

        Counter<int>.Instances = 5;
        Counter<string>.Instances = 99;
        Console.WriteLine($"   Counter<int>.Instances    = {Counter<int>.Instances}");
        Console.WriteLine($"   Counter<string>.Instances = {Counter<string>.Instances}");
        Console.WriteLine("   Counter<int> and Counter<string> are two different types with two");
        Console.WriteLine("   different static fields. This is the mechanism behind the per-type");
        Console.WriteLine("   caches you see inside serializers and DI containers.\n");

        Console.WriteLine("2. The JIT specialises value types and shares reference types.\n");
        Console.WriteLine("   List<int>    -> its own machine code, with int stored inline");
        Console.WriteLine("   List<long>   -> its own machine code again");
        Console.WriteLine("   List<string> -> shared 'canonical' code, used by EVERY reference type");
        Console.WriteLine("   List<Order>  -> the same shared code as List<string>\n");
        Console.WriteLine("   Consequence: 50 generic instantiations over reference types cost one");
        Console.WriteLine("   compilation; 50 over value types cost 50, in both JIT time and code");
        Console.WriteLine("   size. That is the real reason heavily generic code over many struct");
        Console.WriteLine("   types is slow to start under AOT and Blazor WebAssembly.\n");

        Console.WriteLine("3. A generic constraint removes the boxing an interface parameter forces.\n");

        var s = new ShoutingStruct();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            CallViaInterface(s);         // boxes: an interface is a reference
        }

        long viaInterface = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            CallViaConstraint(s);        // constrained call, no box
        }

        long viaConstraint = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"   void M(IShout s)              x10,000 : {viaInterface,8:N0} bytes");
        Console.WriteLine($"   void M<T>(T s) where T : IShout       : {viaConstraint,8:N0} bytes\n");
        Console.WriteLine("   Same call, same method body. The constraint lets the JIT emit a direct");
        Console.WriteLine("   call on the struct; the interface parameter forces a heap copy first.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  iterators — the state machine, and the deferred throw
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static IEnumerable<int> BadValidation(IEnumerable<int> source, int max)
    {
        // THE BUG: this throw does not run when you call the method. It runs on the first
        // MoveNext, which may be in a completely different place — or never.
        if (max < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        foreach (int i in source)
        {
            if (i <= max)
            {
                yield return i;
            }
        }
    }

    private static IEnumerable<int> GoodValidation(IEnumerable<int> source, int max)
    {
        // THE FIX: validate eagerly in a normal method, then delegate to a private iterator.
        ArgumentOutOfRangeException.ThrowIfNegative(max);
        return Iterate(source, max);

        static IEnumerable<int> Iterate(IEnumerable<int> source, int max)
        {
            foreach (int i in source)
            {
                if (i <= max)
                {
                    yield return i;
                }
            }
        }
    }

    private static IEnumerable<string> WithCleanup()
    {
        Console.WriteLine("      [iterator] opening the file");
        try
        {
            yield return "row 1";
            yield return "row 2";
            yield return "row 3";
        }
        finally
        {
            Console.WriteLine("      [iterator] closing the file  <- ran on Dispose");
        }
    }

    private static void Iterators()
    {
        Console.WriteLine("1. `yield return` turns the method into a compiler-generated class.\n");
        Console.WriteLine("   The body becomes a MoveNext() with a `switch (_state)`; the locals");
        Console.WriteLine("   become fields. Calling the method just constructs that object — none of");
        Console.WriteLine("   your code has run yet.\n");

        Console.WriteLine("2. Which means argument validation runs at the wrong time.\n");

        Console.WriteLine("   var q = BadValidation(numbers, -1);   // no exception here...");
        IEnumerable<int> bad = BadValidation([1, 2, 3], -1);
        Console.WriteLine("   ...still nothing. The method has not executed a single line.\n");

        try
        {
            _ = bad.First();
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.WriteLine("   q.First();  ->  ArgumentOutOfRangeException, thrown HERE.");
            Console.WriteLine("   In real code the query is built in a handler and enumerated in a");
            Console.WriteLine("   view, so the stack trace points at the view. Hours vanish here.\n");
        }

        try
        {
            _ = GoodValidation([1, 2, 3], -1);
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.WriteLine("   The fix: an eager wrapper around a private iterator. Now the");
            Console.WriteLine("   exception is thrown at the CALL, before anything is enumerated.\n");
        }

        Console.WriteLine("3. `finally` in an iterator runs on Dispose — which foreach calls for you.\n");

        foreach (string row in WithCleanup())
        {
            Console.WriteLine($"      consumed {row}");
            if (row == "row 2")
            {
                break;      // early exit — the finally still runs, because foreach disposes
            }
        }

        Console.WriteLine("\n   That is why `foreach` compiles to a try/finally around Dispose, and why");
        Console.WriteLine("   a hand-written while (MoveNext()) loop without a `using` leaks whatever");
        Console.WriteLine("   the iterator was holding open.\n");

        Console.WriteLine("4. Laziness lets a sequence be infinite.\n");

        Console.WriteLine($"   Naturals().Where(n => n % 7 == 0).Take(5) = [{string.Join(", ", Naturals().Where(n => n % 7 == 0).Take(5))}]");
        Console.WriteLine("   An infinite sequence is fine as long as nobody asks for all of it.");
        Console.WriteLine("   Call .Count() on it and the process hangs. That is not a bug in LINQ.");

        static IEnumerable<int> Naturals()
        {
            int i = 0;
            while (true)
            {
                yield return i++;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════
    //  expressions — code vs data
    // ═══════════════════════════════════════════════════════════════════════════════════

    private static void Expressions()
    {
        Console.WriteLine("The same lambda, written twice, means two entirely different things.\n");

        Func<int, bool> code = n => n > 10;
        Expression<Func<int, bool>> data = n => n > 10;

        Console.WriteLine($"   Func<int,bool>              : {code}");
        Console.WriteLine("      ...an opaque delegate. You can call it. You cannot read it.\n");

        Console.WriteLine($"   Expression<Func<int,bool>>  : {data}");
        Console.WriteLine($"      Body.NodeType : {data.Body.NodeType}");
        Console.WriteLine($"      Left          : {((BinaryExpression)data.Body).Left}");
        Console.WriteLine($"      Right         : {((BinaryExpression)data.Body).Right}");
        Console.WriteLine("      ...a TREE. You can walk it, rewrite it, or translate it to SQL.\n");

        Console.WriteLine("   That difference IS the difference between IEnumerable and IQueryable:");
        Console.WriteLine("      Where(Func<T,bool>)          -> LINQ to Objects. Runs your delegate.");
        Console.WriteLine("      Where(Expression<Func<..>>)  -> EF Core. Reads the tree, writes SQL.\n");

        Console.WriteLine("   Compiling a tree costs real time, which is why EF caches by tree shape:\n");

        Func<int, bool> compiled = data.Compile();
        Console.WriteLine($"      data.Compile()(42) = {compiled(42)}\n");

        Console.WriteLine("   Building one by hand — this is what a specification or a dynamic filter");
        Console.WriteLine("   does under the covers:\n");

        ParameterExpression p = Expression.Parameter(typeof(int), "n");
        Expression<Func<int, bool>> built = Expression.Lambda<Func<int, bool>>(
            Expression.AndAlso(
                Expression.GreaterThan(p, Expression.Constant(10)),
                Expression.LessThan(p, Expression.Constant(100))),
            p);

        Func<int, bool> builtFunc = built.Compile();
        Console.WriteLine($"      built      : {built}");
        Console.WriteLine($"      built(50)  : {builtFunc(50)}");
        Console.WriteLine($"      built(500) : {builtFunc(500)}\n");

        Console.WriteLine("   See Domain/Common/Specifications/ExpressionExtensions.cs — the And/Or");
        Console.WriteLine("   combinators there are exactly this, plus a rewriter that makes two trees");
        Console.WriteLine("   share one parameter. Without that rewrite EF throws at translation time,");
        Console.WriteLine("   because the second tree refers to a parameter the first has never heard of.");
    }
}
