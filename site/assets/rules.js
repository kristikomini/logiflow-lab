/* ==========================================================================
   rules.js — the viva deck. GENERATED FILE, DO NOT EDIT.

   Source:      course/GOLDEN-RULES.md
   Regenerate:  dotnet run tools/viva-deck.cs

   One card per Golden rule. `kind` is "explain" (say why the claim is true)
   or "complete" (finish the sentence), the second being for the rules the
   course states without a written justification — there has to be something
   on paper to mark yourself against, or self-marking drifts generous.

   `id` is the spaced-repetition key. It is derived from the claim text, so
   reordering the rules costs nothing and rewording one resets that card only.
   ========================================================================== */
window.RULES = [
  {
    "id": "t12-ienumerable-runs-here-iqueryable-runs-there",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "`IEnumerable` runs here; `IQueryable` runs there.",
    "why": "Assigning one to the other silently moves the filtering from the database into your process.",
    "continues": false,
    "checkpoints": [
      "IEnumerable",
      "IQueryable"
    ],
    "refs": [
      "03",
      "16"
    ]
  },
  {
    "id": "t12-a-linq-query-is-a-recipe-not-a-result",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "A LINQ query is a recipe, not a result.",
    "why": "Compose while it is a query, materialise once.",
    "continues": false,
    "refs": [
      "03"
    ]
  },
  {
    "id": "t12-n-1-is-one-query-for-the-parents-and-one-per",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "N+1 is one query for the parents and one per child collection.",
    "why": "Fix with a projection, or `Include`; lazy loading causes it invisibly.",
    "continues": false,
    "checkpoints": [
      "Include"
    ],
    "refs": [
      "06"
    ]
  },
  {
    "id": "t12-equality-columns-first-then-range-and-sort",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "Equality columns first, then range and sort columns",
    "why": "— and never wrap a column in a function.",
    "continues": false,
    "refs": [
      "07"
    ]
  },
  {
    "id": "t12-two-users-one-row-rowversion-409-retry",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "Two users, one row: `rowversion`, 409, retry.",
    "why": "Then a CHECK constraint, because the database is the only guard every writer passes.",
    "continues": false,
    "checkpoints": [
      "rowversion",
      "409"
    ],
    "refs": [
      "07"
    ]
  },
  {
    "id": "t12-async-does-not-create-a-thread-it-frees-one",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "`async` does not create a thread; it frees one.",
    "why": "Never `.Result`, always pass the token.",
    "continues": false,
    "checkpoints": [
      "async",
      ".Result"
    ],
    "refs": [
      "04"
    ]
  },
  {
    "id": "t12-a-singleton-must-never-hold-a-scoped-service",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "A singleton must never hold a scoped service.",
    "why": "Scope validation catches it in Development and is off in Production.",
    "continues": false,
    "refs": [
      "10",
      "15"
    ]
  },
  {
    "id": "t12-the-middleware-pipeline-is-one-nested",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "The middleware pipeline is one nested function built in reverse.",
    "why": "Order is behaviour; authorization sits between matching and executing.",
    "continues": false,
    "refs": [
      "15"
    ]
  },
  {
    "id": "t12-an-aggregate-is-a-consistency-boundary",
    "kind": "complete",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "An aggregate is a consistency boundary; reference other aggregates by id.",
    "stem": "An aggregate is a consistency boundary;",
    "why": "",
    "continues": false,
    "refs": [
      "05"
    ]
  },
  {
    "id": "t12-expected-outcomes-are-result-s-bugs-and",
    "kind": "complete",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "Expected outcomes are `Result`s; bugs and infrastructure throw.",
    "stem": "Expected outcomes are `Result`s;",
    "why": "",
    "continues": false,
    "checkpoints": [
      "Result"
    ],
    "refs": [
      "05"
    ]
  },
  {
    "id": "t12-the-in-memory-provider-is-not-a-database",
    "kind": "explain",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "The in-memory provider is not a database.",
    "why": "No constraints, no transactions, no `rowversion` — it passes tests production fails.",
    "continues": false,
    "checkpoints": [
      "rowversion"
    ],
    "refs": [
      "12"
    ]
  },
  {
    "id": "t12-measure-before-you-optimise-and-read-the-sql",
    "kind": "complete",
    "tier": "twelve",
    "module": "",
    "part": "The twelve that decide interviews",
    "claim": "Measure before you optimise, and read the SQL before you touch the C#.",
    "stem": "Measure before you optimise,",
    "why": "",
    "continues": false,
    "refs": [
      "14"
    ]
  },
  {
    "id": "s6-the-gc-charges-you-for-survivors-not-for",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "The GC charges you for survivors, not for garbage.",
    "why": "A million short-lived objects are cheap; ten thousand that survive are not — so a leak is a reference you forgot, never memory you forgot to free.",
    "continues": false,
    "refs": [
      "19"
    ]
  },
  {
    "id": "s6-the-hash-of-a-key-must-not-change-while-it",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "The hash of a key must not change while it is in the table.",
    "why": "Mutate it and the entry is present, unfindable and unremovable — which is why dictionary keys are immutable.",
    "continues": false,
    "refs": [
      "20"
    ]
  },
  {
    "id": "s6-without-a-fence-another-thread-may-never-see",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "Without a fence, another thread may never see your write.",
    "why": "The compiler can hoist it into a register and the CPU can reorder it; `lock`, `Interlocked` and `volatile` are the fences — and `volatile` still does not make `++` atomic.",
    "continues": false,
    "checkpoints": [
      "lock",
      "Interlocked",
      "volatile",
      "++"
    ],
    "refs": [
      "21"
    ]
  },
  {
    "id": "s6-reflection-is-slow-because-of-the-metadata",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "Reflection is slow because of the metadata lookup and the boxing, not because it is reflection",
    "why": "— so cache the `MemberInfo`, build a delegate, or better, move the work to a source generator at compile time.",
    "continues": false,
    "checkpoints": [
      "MemberInfo"
    ],
    "refs": [
      "22"
    ]
  },
  {
    "id": "s6-an-unqualified-tostring-or-parse-is-a-latent",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "An unqualified `ToString()` or `Parse` is a latent bug on a non-English machine.",
    "why": "On `it-IT`, `\"1234.5\"` parses to `12345`, silently. Ordinal for identifiers, Invariant for persistence, Current for humans.",
    "continues": false,
    "checkpoints": [
      "ToString()",
      "Parse",
      "it-IT",
      "\"1234.5\"",
      "12345"
    ],
    "refs": [
      "23"
    ]
  },
  {
    "id": "s6-you-cannot-atomically-write-to-two-systems",
    "kind": "explain",
    "tier": "senior",
    "module": "",
    "part": "The six that separate a senior candidate",
    "claim": "You cannot atomically write to two systems, and a timeout tells you nothing about whether the work happened.",
    "why": "Hence the outbox, at-least-once delivery, and idempotent consumers.",
    "continues": false,
    "refs": [
      "25"
    ]
  },
  {
    "id": "m00-warnings-are-errors-in-src",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Warnings are errors in `src/`.",
    "why": "A warning nobody reads is a warning that does nothing — and the one it caught here was a real CVE.",
    "continues": false,
    "checkpoints": [
      "src/"
    ]
  },
  {
    "id": "m00-one-version-per-package-declared-once",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "One version per package, declared once.",
    "why": "Central package management turns \"which project is on which version?\" into a question nobody has to ask.",
    "continues": false
  },
  {
    "id": "m00-pin-the-sdk",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Pin the SDK.",
    "why": "`global.json` is the difference between \"works on my machine\" being a joke and being a bug report.",
    "continues": false,
    "checkpoints": [
      "global.json"
    ]
  },
  {
    "id": "m00-newest-is-not-the-goal-patched-and",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Newest is not the goal. Patched and compatible is.",
    "why": "Read the advisory, then pick the version on the major line your framework was built against.",
    "continues": false
  },
  {
    "id": "m00-scope-an-exception-never-turn-a-rule-off",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Scope an exception, never turn a rule off globally.",
    "why": "Generated code gets its own `.editorconfig` section; the code you own keeps the rules.",
    "continues": false,
    "checkpoints": [
      ".editorconfig"
    ]
  },
  {
    "id": "m00-turn-the-sql-log-on-and-leave-it-on",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Turn the SQL log on and leave it on",
    "why": "for modules 06, 07 and 09. Reading the generated SQL is the fastest way to learn what your LINQ costs.",
    "continues": false
  },
  {
    "id": "m00-check-capability-not-environment",
    "kind": "explain",
    "tier": "module",
    "module": "00",
    "part": "Module 00 — Setup and tooling",
    "href": "course/module-00-setup/",
    "claim": "Check capability, not environment.",
    "why": "\"Is a collector configured?\" keeps working when someone runs one locally; \"is this Development?\" does not. It is why Redis and OTLP are optional here.",
    "continues": false
  },
  {
    "id": "m01-record-for-values-class-for-identity-struct",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Record for values, class for identity, struct for small immutable data.",
    "why": "`readonly record struct` is all three properties at once, and it is what a strongly-typed ID should be.",
    "continues": false,
    "checkpoints": [
      "readonly record struct"
    ]
  },
  {
    "id": "m01-a-struct-larger-than-16-bytes-costs-more",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "A struct larger than ~16 bytes costs more than an allocation",
    "why": ", because it is copied on every assignment, argument pass and return.",
    "continues": true
  },
  {
    "id": "m01-readonly-on-a-struct-is-not-decoration",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "`readonly` on a struct is not decoration.",
    "why": "It is what stops the compiler making a defensive copy on every member access through a readonly field or an `in` parameter.",
    "continues": false,
    "checkpoints": [
      "readonly",
      "in"
    ]
  },
  {
    "id": "m01-nullable-reference-types-are-compile-time",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Nullable reference types are compile-time only.",
    "why": "Nothing is checked at runtime, so you still validate at every boundary: JSON, the database, an unannotated library.",
    "continues": false
  },
  {
    "id": "m01-a-switch-expression-over-an-enum-with-no",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "A `switch` expression over an enum with no `default` arm is a tripwire.",
    "why": "Add a member without handling it and the build fails. Adding `default => …` throws that away.",
    "continues": false,
    "checkpoints": [
      "switch",
      "default",
      "default => …"
    ]
  },
  {
    "id": "m01-enum-for-a-closed-set-with-no-data-smart",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Enum for a closed set with no data; smart enum the moment a member carries behaviour or a value",
    "why": "— like JPY having zero decimal places.",
    "continues": false
  },
  {
    "id": "m01-pin-the-numeric-values-of-any-enum-you",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Pin the numeric values of any enum you persist.",
    "why": "Insert a member alphabetically without them and every existing row silently changes meaning.",
    "continues": false
  },
  {
    "id": "m01-static-abstract-interface-members-cannot",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Static abstract interface members cannot appear in an expression tree.",
    "why": "When generic math or a static factory meets EF Core or a mocking library, build the tree by hand with `Expression.New`.",
    "continues": false,
    "checkpoints": [
      "Expression.New"
    ]
  },
  {
    "id": "m01-throw-for-a-developer-mistake-return-a-value",
    "kind": "explain",
    "tier": "module",
    "module": "01",
    "part": "Module 01 — Advanced C#",
    "href": "course/module-01-csharp-advanced/",
    "claim": "Throw for a developer mistake, return a value for a user's.",
    "why": "`Money + Money` in the wrong currency is a wiring bug, not a business outcome — so it throws.",
    "continues": false,
    "checkpoints": [
      "Money + Money"
    ]
  },
  {
    "id": "m02-a-lambda-captures-the-variable-not-its-value",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "A lambda captures the variable, not its value.",
    "why": "It is read when the delegate runs, not when it was written.",
    "continues": false
  },
  {
    "id": "m02-in-a-for-loop-copy-the-loop-variable-into-a",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "In a `for` loop, copy the loop variable into a local before capturing it.",
    "why": "`foreach` was fixed in C# 5; `for` was not, and that inconsistency is why the bug survives.",
    "continues": false,
    "checkpoints": [
      "for",
      "foreach"
    ]
  },
  {
    "id": "m02-capturing-allocates",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Capturing allocates.",
    "why": "A capture-free lambda is cached and allocates once per process; one that captures allocates a display class every time it is created.",
    "continues": false
  },
  {
    "id": "m02-use-static-lambdas-on-hot-paths",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Use `static` lambdas on hot paths",
    "why": "and pass state as an argument — capture becomes a compile error instead of a silent allocation.",
    "continues": false,
    "checkpoints": [
      "static"
    ]
  },
  {
    "id": "m02-func-is-code-you-can-only-run-expression",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "`Func` is code you can only run. `Expression<Func>` is data describing that code.",
    "why": "Identical syntax; the target type decides which the compiler emits.",
    "continues": false,
    "checkpoints": [
      "Func",
      "Expression<Func>"
    ]
  },
  {
    "id": "m02-never-hand-a-compiled-func-to-a-query",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Never hand a compiled `Func` to a query provider.",
    "why": "EF Core cannot see inside a delegate, so it fetches every row and filters in memory.",
    "continues": false,
    "checkpoints": [
      "Func"
    ]
  },
  {
    "id": "m02-combine-predicates-by-rewriting-the",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Combine predicates by rewriting the parameter node with an `ExpressionVisitor`",
    "why": ", then `Expression.AndAlso`. `a.Compile()(x) && b.Compile()(x)` compiles and then throws at query time.",
    "continues": true,
    "checkpoints": [
      "ExpressionVisitor",
      "Expression.AndAlso",
      "a.Compile()(x) && b.Compile()(x)"
    ]
  },
  {
    "id": "m02-expression-trees-are-immutable",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Expression trees are immutable.",
    "why": "A visitor returns a new tree; it does not edit the old one.",
    "continues": false
  },
  {
    "id": "m02-every-on-a-long-lived-event-needs-a",
    "kind": "explain",
    "tier": "module",
    "module": "02",
    "part": "Module 02 — Delegates, lambdas and closures",
    "href": "course/module-02-delegates-and-closures/",
    "claim": "Every `+=` on a long-lived event needs a `-=`.",
    "why": "A publisher holding a delegate to a dead subscriber is the most common managed memory leak in .NET.",
    "continues": false,
    "checkpoints": [
      "+=",
      "-="
    ]
  },
  {
    "id": "m03-ienumerable-t-takes-delegates-and-runs-here",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "`IEnumerable<T>` takes delegates and runs here. `IQueryable<T>` takes expression trees and runs there.",
    "why": "Assigning one to the other silently moves the work — and the whole table.",
    "continues": false,
    "checkpoints": [
      "IEnumerable<T>",
      "IQueryable<T>"
    ]
  },
  {
    "id": "m03-compose-while-it-is-a-query-materialise-once",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "Compose while it is a query; materialise once, at the end.",
    "why": "Every `ToList()` in the middle of a chain is a decision to stop using the database.",
    "continues": false,
    "checkpoints": [
      "ToList()"
    ]
  },
  {
    "id": "m03-a-query-is-a-recipe-not-a-result",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "A query is a recipe, not a result.",
    "why": "It re-runs on every enumeration, and it reads captured variables at execution time, not at definition time.",
    "continues": false
  },
  {
    "id": "m03-if-it-returns-a-sequence-it-is-lazy-if-it",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "If it returns a sequence it is lazy; if it returns a value or a collection it already ran.",
    "why": "That one rule covers every operator you will meet.",
    "continues": false
  },
  {
    "id": "m03-never-enumerate-twice-by-accident",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "Never enumerate twice by accident.",
    "why": "`.Count()` then `foreach` is two round trips to the database for one answer.",
    "continues": false,
    "checkpoints": [
      ".Count()",
      "foreach"
    ]
  },
  {
    "id": "m03-validate-eagerly-yield-lazily",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "Validate eagerly, yield lazily.",
    "why": "Argument checks go in a wrapper method that returns the iterator — otherwise they throw at enumeration, somewhere else entirely.",
    "continues": false
  },
  {
    "id": "m03-never-return-an-iqueryable-t-past-the",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "Never return an `IQueryable<T>` past the lifetime of its `DbContext`.",
    "why": "The caller enumerates it after disposal and gets an exception with nothing useful in it.",
    "continues": false,
    "checkpoints": [
      "IQueryable<T>",
      "DbContext"
    ]
  },
  {
    "id": "m03-when-ef-core-says-it-cannot-translate-it-is",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "When EF Core says it cannot translate, it is doing you a favour.",
    "why": "Before 3.0 it silently fetched the table instead, and people found out in production.",
    "continues": false
  },
  {
    "id": "m03-single-is-not-a-stricter-first",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "`Single` is not a stricter `First`.",
    "why": "It must scan the whole source to prove there is no second match.",
    "continues": false,
    "checkpoints": [
      "Single",
      "First"
    ]
  },
  {
    "id": "m03-read-the-generated-sql",
    "kind": "explain",
    "tier": "module",
    "module": "03",
    "part": "Module 03 — LINQ internals",
    "href": "course/module-03-linq-internals/",
    "claim": "Read the generated SQL.",
    "why": "Every belief you hold about what a query costs is a hypothesis until you have.",
    "continues": false
  },
  {
    "id": "m04-async-does-not-create-a-thread-it-frees-one",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "`async` does not create a thread. It frees one.",
    "why": "A server handling 10,000 idle connections needs roughly zero threads, not 10,000.",
    "continues": false,
    "checkpoints": [
      "async"
    ]
  },
  {
    "id": "m04-async-all-the-way-down",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "Async all the way down.",
    "why": "`.Result`, `.Wait()` and `.GetAwaiter().GetResult()` deadlock where there is a synchronisation context and starve the thread pool where there is not — which fails under load instead of immediately, so you ship it.",
    "continues": false,
    "checkpoints": [
      ".Result",
      ".Wait()",
      ".GetAwaiter().GetResult()"
    ]
  },
  {
    "id": "m04-async-void-only-for-a-literal-event-handler",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "`async void` only for a literal event handler.",
    "why": "Anywhere else the exception has no `Task` to land in and takes the process down.",
    "continues": false,
    "checkpoints": [
      "async void",
      "Task"
    ]
  },
  {
    "id": "m04-pass-the-cancellationtoken-always-all-the",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "Pass the `CancellationToken`, always, all the way to the driver.",
    "why": "A user who closed their browser should stop costing you a connection.",
    "continues": false,
    "checkpoints": [
      "CancellationToken"
    ]
  },
  {
    "id": "m04-a-cancelled-request-is-499-not-500",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "A cancelled request is 499, not 500.",
    "why": "Logging the client's departure as your failure pollutes the error budget you actually need.",
    "continues": false,
    "checkpoints": [
      "499"
    ]
  },
  {
    "id": "m04-configureawait-false-in-library-code",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "`ConfigureAwait(false)` in library code; unnecessary in ASP.NET Core application code",
    "why": ", which has no synchronisation context to capture.",
    "continues": true,
    "checkpoints": [
      "ConfigureAwait(false)"
    ]
  },
  {
    "id": "m04-await-is-sequential",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "`await` is sequential.",
    "why": "To run two things at once, start both, then `await Task.WhenAll` — and remember it surfaces only the first exception.",
    "continues": false,
    "checkpoints": [
      "await",
      "await Task.WhenAll"
    ]
  },
  {
    "id": "m04-dbcontext-is-not-thread-safe",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "`DbContext` is not thread-safe.",
    "why": "Concurrent queries need a context each, from `IDbContextFactory`.",
    "continues": false,
    "checkpoints": [
      "DbContext",
      "IDbContextFactory"
    ]
  },
  {
    "id": "m04-a-backgroundservice-is-a-singleton",
    "kind": "explain",
    "tier": "module",
    "module": "04",
    "part": "Module 04 — Async and concurrency",
    "href": "course/module-04-async/",
    "claim": "A `BackgroundService` is a singleton.",
    "why": "No scoped services in its constructor: take `IServiceScopeFactory` and open a scope per iteration, or you have built a memory leak that also serves stale data.",
    "continues": false,
    "checkpoints": [
      "BackgroundService",
      "IServiceScopeFactory"
    ]
  },
  {
    "id": "m05-dependencies-point-inward-and-a-test-says-so",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Dependencies point inward, and a test says so.",
    "why": "The Domain project has no package and no project references; `LayeringTests` keeps it that way after you have stopped watching.",
    "continues": false,
    "checkpoints": [
      "LayeringTests"
    ]
  },
  {
    "id": "m05-inner-layers-declare-what-they-need-outer",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Inner layers declare what they need; outer layers supply it.",
    "why": "That is dependency inversion — not \"we use a DI container\", which is a different thing people routinely confuse it with.",
    "continues": false
  },
  {
    "id": "m05-identity-means-entity-attributes-mean-value",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Identity means entity. Attributes mean value object.",
    "why": "Two customers with the same name are two customers; two addresses with the same fields are one address.",
    "continues": false
  },
  {
    "id": "m05-an-aggregate-is-a-consistency-boundary-not-a",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "An aggregate is a consistency boundary, not a folder.",
    "why": "Everything inside it is saved in one transaction, and its invariants are true at the end of every one.",
    "continues": false
  },
  {
    "id": "m05-reference-other-aggregates-by-id",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Reference other aggregates by id.",
    "why": "An object reference is an invitation to mutate two aggregates in one transaction — which is why the mapping here withholds the navigation.",
    "continues": false
  },
  {
    "id": "m05-keep-aggregates-small",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Keep aggregates small.",
    "why": "If two things need not be consistent *immediately*, they belong apart and an event reconciles them.",
    "continues": false
  },
  {
    "id": "m05-expose-read-only-collections",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Expose read-only collections.",
    "why": "A public `List<T>` bypasses every rule in `AddLine` and every domain event, and the aggregate becomes decoration.",
    "continues": false,
    "checkpoints": [
      "List<T>",
      "AddLine"
    ]
  },
  {
    "id": "m05-if-a-human-could-reasonably-cause-it-return",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "If a human could reasonably cause it, return a `Result`. If only a bug or the infrastructure could, throw.",
    "why": "Roughly 4,500x cheaper here — but the real win is that failure is in the signature and the compiler makes callers acknowledge it.",
    "continues": false,
    "checkpoints": [
      "Result"
    ]
  },
  {
    "id": "m05-clients-branch-on-a-stable-code-never-on-a",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Clients branch on a stable `Code`, never on a message.",
    "why": "The message is free to change and to be translated.",
    "continues": false,
    "checkpoints": [
      "Code"
    ]
  },
  {
    "id": "m05-events-are-facts-in-the-past-tense",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Events are facts, in the past tense.",
    "why": "`OrderPlacedDomainEvent`, not `PlaceOrderEvent`. If you want to reject one, you modelled a command.",
    "continues": false,
    "checkpoints": [
      "OrderPlacedDomainEvent",
      "PlaceOrderEvent"
    ]
  },
  {
    "id": "m05-keep-the-state-machine-as-data-in-one-table",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Keep the state machine as data, in one table.",
    "why": "A guard clause at the top of eight methods means someone eventually updates seven of them.",
    "continues": false
  },
  {
    "id": "m05-never-double-for-money",
    "kind": "explain",
    "tier": "module",
    "module": "05",
    "part": "Module 05 — Clean Architecture and Domain-Driven Design",
    "href": "course/module-05-clean-architecture/",
    "claim": "Never `double` for money.",
    "why": "`decimal` is base-10 and stores 0.1 exactly; binary floating point cannot.",
    "continues": false,
    "checkpoints": [
      "double",
      "decimal"
    ]
  },
  {
    "id": "m06-tracking-for-writes-asnotracking-for-reads",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Tracking for writes, `AsNoTracking()` for reads.",
    "why": "Snapshotting entities you will never change costs 20–30% and more memory, for nothing.",
    "continues": false,
    "checkpoints": [
      "AsNoTracking()"
    ]
  },
  {
    "id": "m06-dbcontext-is-a-session-scoped-and-not-thread",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "`DbContext` is a session: scoped, and not thread-safe.",
    "why": "Register it as a singleton and you get a bug that appears only under load, only in production.",
    "continues": false,
    "checkpoints": [
      "DbContext"
    ]
  },
  {
    "id": "m06-configure-with-the-fluent-api-not-attributes",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Configure with the Fluent API, not attributes.",
    "why": "It keeps persistence out of the Domain and expresses what attributes cannot: converters, filtered indexes, check constraints.",
    "continues": false
  },
  {
    "id": "m06-set-decimal-precision-explicitly",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Set `decimal` precision explicitly.",
    "why": "The default silently truncates, and you find out when the accounts do not balance.",
    "continues": false,
    "checkpoints": [
      "decimal"
    ]
  },
  {
    "id": "m06-value-objects-map-inline-as-columns",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Value objects map inline, as columns",
    "why": "— not as a table with its own id and orphan rows.",
    "continues": false
  },
  {
    "id": "m06-be-explicit-about-what-you-load",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Be explicit about what you load.",
    "why": "`Include` or project; never lazy loading, which turns a property read into a query and makes N+1 invisible.",
    "continues": false,
    "checkpoints": [
      "Include"
    ]
  },
  {
    "id": "m06-prefer-a-projection-to-include-for-reads",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Prefer a projection to `Include` for reads.",
    "why": "Fetch the six columns the screen needs, not the aggregate and all its children.",
    "continues": false,
    "checkpoints": [
      "Include"
    ]
  },
  {
    "id": "m06-never-query-inside-a-foreach",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Never query inside a `foreach`.",
    "why": "One `WHERE Id IN (…)` — and mind SQL Server's 2,100-parameter ceiling.",
    "continues": false,
    "checkpoints": [
      "foreach",
      "WHERE Id IN (…)",
      "100"
    ]
  },
  {
    "id": "m06-per-aggregate-repositories-with-intention",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Per-aggregate repositories, with intention-revealing names.",
    "why": "`GetWithLinesAsync` tells the caller what it loads; a generic `GetById` lets them find out when a navigation is null.",
    "continues": false,
    "checkpoints": [
      "GetWithLinesAsync",
      "GetById"
    ]
  },
  {
    "id": "m06-anything-that-must-never-be-forgotten",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Anything that must never be forgotten belongs in an interceptor.",
    "why": "The handler someone forgets to call is the one whose events silently never fire.",
    "continues": false
  },
  {
    "id": "m06-anything-crossing-a-process-boundary-goes",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Anything crossing a process boundary goes through the outbox.",
    "why": "You cannot commit two systems atomically, so make it one write and publish afterwards.",
    "continues": false
  },
  {
    "id": "m06-read-every-generated-migration-and-never",
    "kind": "explain",
    "tier": "module",
    "module": "06",
    "part": "Module 06 — EF Core in depth",
    "href": "course/module-06-efcore/",
    "claim": "Read every generated migration, and never edit an applied one.",
    "why": "EF sometimes decides a rename is a drop-and-recreate, which silently deletes a column of data.",
    "continues": false
  },
  {
    "id": "m07-equality-columns-first-then-range-and-sort",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "Equality columns first, then range and sort columns.",
    "why": "The leftmost-prefix rule decides whether your index gets used at all.",
    "continues": false
  },
  {
    "id": "m07-a-function-around-a-column-kills-the-seek",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "A function around a column kills the seek.",
    "why": "Compare the column raw; normalise the *parameter* instead.",
    "continues": false
  },
  {
    "id": "m07-in-sql-server-several-nulls-collide-in-a",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "In SQL Server, several NULLs collide in a unique index.",
    "why": "Nullable uniqueness needs a filtered index — `WHERE [Col] IS NOT NULL`.",
    "continues": false,
    "checkpoints": [
      "WHERE [Col] IS NOT NULL"
    ]
  },
  {
    "id": "m07-with-nolock-is-read-uncommitted-not-a",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "`WITH (NOLOCK)` is Read Uncommitted, not a performance switch.",
    "why": "It can read a row twice, skip it entirely, or return data that was rolled back. If you need non-blocking reads, use snapshot isolation.",
    "continues": false,
    "checkpoints": [
      "WITH (NOLOCK)"
    ]
  },
  {
    "id": "m07-optimistic-concurrency-for-web-traffic",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "Optimistic concurrency for web traffic.",
    "why": "No lock survives a stateless HTTP request: detect the clash with `rowversion`, return 409, retry.",
    "continues": false,
    "checkpoints": [
      "rowversion",
      "409"
    ]
  },
  {
    "id": "m07-a-real-guarantee-needs-three-layers",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "A real guarantee needs three layers",
    "why": "— the aggregate for a friendly error, `rowversion` for the race, and a CHECK constraint because the database is the only guard that every writer passes, including a bad migration and a DBA at 2am.",
    "continues": false,
    "checkpoints": [
      "rowversion"
    ]
  },
  {
    "id": "m07-count-before-skip-take",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "`COUNT` before `Skip`/`Take`",
    "why": ", or every client is told it is on page 1 of 1.",
    "continues": true,
    "checkpoints": [
      "COUNT",
      "Skip",
      "Take"
    ]
  },
  {
    "id": "m07-every-paginated-order-by-needs-a-unique",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "Every paginated `ORDER BY` needs a unique tiebreaker.",
    "why": "Without one, `OFFSET/FETCH` can show a row on two pages and another on none. Almost every paginated endpoint in the wild has this bug.",
    "continues": false,
    "checkpoints": [
      "ORDER BY",
      "OFFSET/FETCH"
    ]
  },
  {
    "id": "m07-keyset-for-infinite-scroll-offset-for-page",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "Keyset for infinite scroll, offset for page numbers.",
    "why": "Offset gets slower with depth because the server produces and discards every preceding row.",
    "continues": false
  },
  {
    "id": "m07-select-max-id-1-is-a-race",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "`SELECT MAX(id) + 1` is a race.",
    "why": "Use a sequence — and know that a sequence is not gapless, which matters where invoice numbering is a legal requirement.",
    "continues": false,
    "checkpoints": [
      "SELECT MAX(id) + 1"
    ]
  },
  {
    "id": "m07-with-enableretryonfailure-hand-the-whole",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "With `EnableRetryOnFailure`, hand the whole transaction to `IExecutionStrategy`",
    "why": "— and make that block idempotent, because it may genuinely run twice.",
    "continues": false,
    "checkpoints": [
      "EnableRetryOnFailure",
      "IExecutionStrategy"
    ]
  },
  {
    "id": "m07-look-at-the-execution-plan-before-changing",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "Look at the execution plan before changing anything.",
    "why": "Seek is good, scan on a large table is not, and a key lookup means you are one `INCLUDE` away from a covering index.",
    "continues": false,
    "checkpoints": [
      "INCLUDE"
    ]
  },
  {
    "id": "m07-a-view-is-a-saved-select-not-saved-data",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "A view is a saved `SELECT`, not saved data.",
    "why": "It costs what the full query costs; what it buys is a stable contract and a permission boundary.",
    "continues": false,
    "checkpoints": [
      "SELECT"
    ]
  },
  {
    "id": "m07-a-scalar-udf-runs-once-per-row-and-lies-in",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "A scalar UDF runs once per row and lies in the plan.",
    "why": "The optimiser cannot see inside it, so it costs the call at nearly nothing — the one place this module's \"trust the plan\" advice breaks. Use an inline table-valued function instead.",
    "continues": false
  },
  {
    "id": "m07-ef-s-raw-sql-helpers-are-composable-query",
    "kind": "explain",
    "tier": "module",
    "module": "07",
    "part": "Module 07 — SQL, transactions and concurrency",
    "href": "course/module-07-sql-and-transactions/",
    "claim": "EF's raw-SQL helpers are composable query builders.",
    "why": "That single fact explains both why `NEXT VALUE FOR` fails inside `SqlQuery<T>` and why you cannot put a `Where` after an `EXEC`.",
    "continues": false,
    "checkpoints": [
      "NEXT VALUE FOR",
      "SqlQuery<T>",
      "Where",
      "EXEC"
    ]
  },
  {
    "id": "m08-commands-change-state-and-return-as-little",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Commands change state and return as little as possible; queries change nothing and return a purpose-built DTO.",
    "why": "Naming follows: imperative for one, interrogative for the other.",
    "continues": false
  },
  {
    "id": "m08-cqrs-is-not-two-databases-not-event-sourcing",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "CQRS is not two databases, not event sourcing, not microservices.",
    "why": "It is two models. Being able to say that is half the interview question.",
    "continues": false
  },
  {
    "id": "m08-never-load-aggregates-to-render-a-list",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Never load aggregates to render a list.",
    "why": "Twenty-five aggregates with all their children, registered with the change tracker, to display six columns, is why people conclude that Clean Architecture is slow.",
    "continues": false
  },
  {
    "id": "m08-everything-that-changes-together-lives",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Everything that changes together lives together.",
    "why": "Command, validator and handler in one file; retiring a feature is deleting a file, not archaeology.",
    "continues": false
  },
  {
    "id": "m08-pipeline-order-is-behaviour-not-style",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Pipeline order is behaviour, not style.",
    "why": "Logging outermost so its timing covers everything; validation before caching; caching before the transaction; the transaction innermost so it is held for the shortest possible time.",
    "continues": false
  },
  {
    "id": "m08-validators-check-shape-the-domain-checks",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Validators check shape; the domain checks state.",
    "why": "A validator that queries the database has become a handler — and its answer is stale by the time the handler runs anyway.",
    "continues": false
  },
  {
    "id": "m08-cache-by-opt-in-never-automatically",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Cache by opt-in, never automatically.",
    "why": "A behaviour that cached everything would eventually cache something that must be fresh, and that bug is invisible until a customer sees it.",
    "continues": false
  },
  {
    "id": "m08-a-cache-key-must-contain-every-parameter",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "A cache key must contain every parameter that changes the answer",
    "why": "— including the tenant or user when the result is scoped to a caller. Leaking one customer's data to another through a shared key is a regularly-shipped security bug.",
    "continues": false
  },
  {
    "id": "m08-some-things-are-never-cached",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Some things are never cached.",
    "why": "Stock levels, above all: stale stock means orders you cannot fulfil.",
    "continues": false
  },
  {
    "id": "m08-handlers-stay-internal",
    "kind": "explain",
    "tier": "module",
    "module": "08",
    "part": "Module 08 — CQRS and the mediator pattern",
    "href": "course/module-08-cqrs/",
    "claim": "Handlers stay `internal`.",
    "why": "Nothing outside the Application layer should be able to call one directly and skip the pipeline.",
    "continues": false,
    "checkpoints": [
      "internal"
    ]
  },
  {
    "id": "m09-aggregate-over-many-rows-in-sql-apply",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Aggregate over many rows in SQL; apply business rules to the few rows you fetched, in C#.",
    "why": "The database is better at the first and has no idea about the second.",
    "continues": false
  },
  {
    "id": "m09-in-sql-a-group-is-its-key-and-its-aggregates",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "In SQL, a group is its key and its aggregates. The rows are gone.",
    "why": "Asking for the members throws, and that is EF Core 3.0+ protecting you.",
    "continues": false
  },
  {
    "id": "m09-cast-to-nullable-before-sum",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Cast to nullable before `Sum`.",
    "why": "SQL's `SUM` over zero rows is `NULL`, not `0`: `Sum(x => (decimal?)x.Amount) ?? 0m`.",
    "continues": false,
    "checkpoints": [
      "Sum",
      "NULL"
    ]
  },
  {
    "id": "m09-a-where-before-groupby-is-a-where-after-the",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "A `Where` before `GroupBy` is a `WHERE`; after the aggregate projection it is a `HAVING`.",
    "why": "Two different questions that look almost identical in C#.",
    "continues": false,
    "checkpoints": [
      "Where",
      "GroupBy",
      "HAVING"
    ]
  },
  {
    "id": "m09-count-the-distinct-thing-you-mean",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Count the distinct thing you mean.",
    "why": "Counting lines when you meant orders produces a plausible wrong number, which is the worst kind of reporting bug.",
    "continues": false
  },
  {
    "id": "m09-six-aggregates-over-one-grouping-cost-one",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Six aggregates over one grouping cost one scan.",
    "why": "Six queries for six numbers is the mistake this avoids.",
    "continues": false
  },
  {
    "id": "m09-grouping-on-a-computed-key-cannot-use-an",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Grouping on a computed key cannot use an index on the underlying column.",
    "why": "Fine for a report, wrong for a hot path.",
    "continues": false
  },
  {
    "id": "m09-a-predicate-comparing-two-columns-can-never",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "A predicate comparing two columns can never seek.",
    "why": "If it matters, add a persisted computed column and index that.",
    "continues": false
  },
  {
    "id": "m09-compose-filters-conditionally-never-write",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Compose filters conditionally; never write `WHERE (@x IS NULL OR Col = @x)`.",
    "why": "One cached plan for every combination is usually a bad plan for all of them.",
    "continues": false,
    "checkpoints": [
      "WHERE (@x IS NULL OR Col = @x)"
    ]
  },
  {
    "id": "m09-never-accept-a-sort-or-filter-column-as-free",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Never accept a sort or filter column as free text.",
    "why": "A `switch` over an enum is compile-time checked, cannot be handed an arbitrary column name, and fails the build when you add a field and forget it.",
    "continues": false,
    "checkpoints": [
      "switch"
    ]
  },
  {
    "id": "m09-read-the-generated-sql-for-every-report",
    "kind": "explain",
    "tier": "module",
    "module": "09",
    "part": "Module 09 — Advanced LINQ",
    "href": "course/module-09-advanced-linq/",
    "claim": "Read the generated SQL for every report query you write.",
    "why": "Reports are where a single careless operator turns into a table scan nobody notices for a year.",
    "continues": false
  },
  {
    "id": "m10-transient-per-resolution-scoped-per-request",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Transient per resolution, scoped per request, singleton per process.",
    "why": "`DbContext` is scoped because it is a unit of work and is not thread-safe.",
    "continues": false,
    "checkpoints": [
      "DbContext"
    ]
  },
  {
    "id": "m10-a-singleton-must-never-hold-a-scoped-service",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "A singleton must never hold a scoped service.",
    "why": "If it needs one, inject `IServiceScopeFactory` and open a scope per unit of work.",
    "continues": false,
    "checkpoints": [
      "IServiceScopeFactory"
    ]
  },
  {
    "id": "m10-each-layer-registers-itself",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Each layer registers itself.",
    "why": "Otherwise `Program.cs` becomes a 300-line permanent merge conflict.",
    "continues": false,
    "checkpoints": [
      "Program.cs",
      "300"
    ]
  },
  {
    "id": "m10-an-endpoint-binds-dispatches-and-maps-the",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "An endpoint binds, dispatches and maps the result.",
    "why": "An `if` about business state inside an endpoint is a smell.",
    "continues": false,
    "checkpoints": [
      "if"
    ]
  },
  {
    "id": "m10-a-non-nullable-property-is-a-required",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "A non-nullable property is a required parameter.",
    "why": "`[AsParameters]` never sees your property initialiser — make optional query parameters nullable and resolve the default yourself.",
    "continues": false,
    "checkpoints": [
      "[AsParameters]"
    ]
  },
  {
    "id": "m10-post-to-create-post-a-verb-sub-resource-for",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "POST to create; POST a verb sub-resource for a state transition; PATCH for a partial update",
    "why": "— and a 201 always carries a `Location` header.",
    "continues": false,
    "checkpoints": [
      "Location",
      "201"
    ]
  },
  {
    "id": "m10-errors-are-rfc-9457-problem-details-with-a",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Errors are RFC 9457 Problem Details with a stable `code`.",
    "why": "Clients branch on the code, never on the human-readable detail.",
    "continues": false,
    "checkpoints": [
      "code"
    ]
  },
  {
    "id": "m10-401-is-i-do-not-know-who-you-are-403-is-i",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "401 is \"I do not know who you are\"; 403 is \"I know, and no\".",
    "why": "400 is malformed; 409 is well-formed and conflicting. An empty search result is 200 with `[]`.",
    "continues": false,
    "checkpoints": [
      "[]",
      "401",
      "403",
      "400",
      "409",
      "200"
    ]
  },
  {
    "id": "m10-a-binding-failure-is-the-caller-s-400-not",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "A binding failure is the caller's 400, not your 500.",
    "why": "Otherwise your error alerting fills up with other people's typos.",
    "continues": false,
    "checkpoints": [
      "400"
    ]
  },
  {
    "id": "m10-never-return-a-stack-trace",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Never return a stack trace.",
    "why": "The detail goes to the log; the client gets a `traceId`.",
    "continues": false,
    "checkpoints": [
      "traceId"
    ]
  },
  {
    "id": "m10-a-cache-failure-must-degrade-never-fail",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "A cache failure must degrade, never fail.",
    "why": "Redis down means a cache miss and a slower response, not an error page.",
    "continues": false
  },
  {
    "id": "m10-rate-limit-before-authentication",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Rate limit before authentication, authenticate before authorise, and register exception handling first.",
    "why": "Middleware order is behaviour, not style.",
    "continues": false
  },
  {
    "id": "m10-validateonstart-on-everything-the",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "`ValidateOnStart` on everything the application cannot run without.",
    "why": "A missing signing key should stop the process from starting, not produce 500s an hour later.",
    "continues": false,
    "checkpoints": [
      "ValidateOnStart"
    ]
  },
  {
    "id": "m10-queue-email-inside-the-transaction-deliver",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Queue email inside the transaction; deliver it outside.",
    "why": "Sending during a transaction holds locks on a mail server's schedule and mails customers about orders that then roll back.",
    "continues": false
  },
  {
    "id": "m10-at-least-once-delivery-means-the-message",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "At-least-once delivery means the message needs an idempotency key derived from the fact",
    "why": "— never from `Guid.NewGuid()`, which is the same as having no key.",
    "continues": false,
    "checkpoints": [
      "Guid.NewGuid()"
    ]
  },
  {
    "id": "m10-claim-queue-rows-atomically-updlock-readpast",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Claim queue rows atomically (`UPDLOCK, READPAST`, `OUTPUT`) and count the attempt when you claim",
    "why": ", or two instances send everything twice and a poison message is immortal.",
    "continues": true,
    "checkpoints": [
      "UPDLOCK, READPAST",
      "OUTPUT"
    ]
  },
  {
    "id": "m10-retry-4xx-never-5xx-exponentially-with",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Retry 4xx, never 5xx, exponentially with jitter, then dead-letter and keep it.",
    "why": "A cleanup job that deletes the evidence of its own failures is worse than none.",
    "continues": false
  },
  {
    "id": "m10-redirect-all-outbound-mail-in-every-non",
    "kind": "explain",
    "tier": "module",
    "module": "10",
    "part": "Module 10 — Cross-cutting concerns",
    "href": "course/module-10-cross-cutting/",
    "claim": "Redirect all outbound mail in every non-production environment.",
    "why": "The incident this prevents happens somewhere every month and is always found by the customers.",
    "continues": false
  },
  {
    "id": "m11-metrics-to-detect-traces-to-localise-logs-to",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Metrics to detect, traces to localise, logs to diagnose.",
    "why": "Using logs for metrics is slow and expensive; using metrics for diagnosis is impossible.",
    "continues": false
  },
  {
    "id": "m11-log-a-message-template-plus-named-properties",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Log a message template plus named properties, never an interpolated string.",
    "why": "The first arrives as indexed fields you can group by; the second is a million unique strings.",
    "continues": false
  },
  {
    "id": "m11-use-scopes-for-correlation",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Use scopes for correlation.",
    "why": "One id on every line written anywhere inside the request, including deep inside EF Core.",
    "continues": false
  },
  {
    "id": "m11-a-rejected-order-is-a-warning-not-an-error",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "A rejected order is a Warning, not an Error.",
    "why": "Logging business rejections at Error is how a team learns to ignore its own error log, and alert fatigue is an outage waiting to happen.",
    "continues": false
  },
  {
    "id": "m11-never-log-secrets-and-guard",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Never log secrets, and guard `EnableSensitiveDataLogging` with an environment check",
    "why": "— not with an intention to remember. It logs parameter values, which under GDPR includes names, emails and addresses.",
    "continues": false,
    "checkpoints": [
      "EnableSensitiveDataLogging"
    ]
  },
  {
    "id": "m11-instrument-with-opentelemetry-so-the-vendor",
    "kind": "complete",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Instrument with OpenTelemetry so the vendor becomes a configuration change",
    "stem": "Instrument with OpenTelemetry so the",
    "why": "",
    "continues": true
  },
  {
    "id": "m11-liveness-must-not-check-dependencies",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Liveness must not check dependencies. Readiness must.",
    "why": "Backwards, and a brief database problem becomes a restart storm.",
    "continues": false
  },
  {
    "id": "m11-alert-on-symptoms-users-feel",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Alert on symptoms users feel",
    "why": "— error rate, p99 latency, a business metric flatlining — not on CPU or memory. Latency, traffic, errors and saturation are the four to start from.",
    "continues": false
  },
  {
    "id": "m11-every-alert-must-be-actionable",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Every alert must be actionable.",
    "why": "One that fires and is routinely ignored should be deleted: it is training you to ignore the others.",
    "continues": false
  },
  {
    "id": "m11-store-a-timestamp-not-a-boolean",
    "kind": "explain",
    "tier": "module",
    "module": "11",
    "part": "Module 11 — Observability",
    "href": "course/module-11-observability/",
    "claim": "Store a timestamp, not a boolean.",
    "why": "A nullable `ProcessedAtUtc` lets you alert on *how far behind* the outbox is; an `IsProcessed` flag can only say whether.",
    "continues": false,
    "checkpoints": [
      "ProcessedAtUtc",
      "IsProcessed"
    ]
  },
  {
    "id": "m12-many-fast-tests-few-slow-ones",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Many fast tests, few slow ones.",
    "why": "Invert the pyramid and the suite takes forty minutes, so nobody runs it, so it catches nothing.",
    "continues": false
  },
  {
    "id": "m12-a-domain-test-needs-no-mocks-no-database-and",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "A domain test needs no mocks, no database and no `async`.",
    "why": "If yours does, the design is telling you something.",
    "continues": false,
    "checkpoints": [
      "async"
    ]
  },
  {
    "id": "m12-mock-what-you-do-not-control-use-the-real",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Mock what you do not control; use the real thing for what you own.",
    "why": "Never mock your own domain objects.",
    "continues": false
  },
  {
    "id": "m12-name-tests-method-scenario-expectedoutcome",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Name tests `Method_Scenario_ExpectedOutcome`.",
    "why": "A failure should be diagnosable from the name alone, without opening the file.",
    "continues": false,
    "checkpoints": [
      "Method_Scenario_ExpectedOutcome"
    ]
  },
  {
    "id": "m12-assert-the-absence-of-side-effects-too",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Assert the absence of side effects too.",
    "why": "\"It failed\" is half the assertion; \"…and staged nothing for insertion\" is the other half.",
    "continues": false
  },
  {
    "id": "m12-useinmemorydatabase-is-not-a-database",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "`UseInMemoryDatabase` is not a database.",
    "why": "No schema, no unique indexes, no CHECK constraints, no transactions, no `rowversion`, no raw SQL — so it passes tests that production fails. Use a real SQL Server and give each run its own throwaway database.",
    "continues": false,
    "checkpoints": [
      "UseInMemoryDatabase",
      "rowversion"
    ]
  },
  {
    "id": "m12-assert-against-the-database-not-the-api",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Assert against the database, not the API.",
    "why": "Asking the API whether the API worked is circular.",
    "continues": false
  },
  {
    "id": "m12-declare-wire-contracts-separately-in-the",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Declare wire contracts separately in the test project.",
    "why": "A test that shares the production DTO cannot detect a breaking change to the contract.",
    "continues": false
  },
  {
    "id": "m12-test-outcomes-not-interactions",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Test outcomes, not interactions.",
    "why": "\"Submitting reserves stock\" survives a refactor; \"it called `Reserve` once with those arguments\" breaks on the next one.",
    "continues": false,
    "checkpoints": [
      "Reserve"
    ]
  },
  {
    "id": "m12-turn-conventions-into-build-failures",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Turn conventions into build failures.",
    "why": "Architecture tests cost milliseconds and keep the design honest through years of team turnover — but make the failure message name the offender, or people will delete the test.",
    "continues": false
  },
  {
    "id": "m12-do-not-chase-coverage",
    "kind": "explain",
    "tier": "module",
    "module": "12",
    "part": "Module 12 — Testing",
    "href": "course/module-12-testing/",
    "claim": "Do not chase coverage.",
    "why": "100% with weak assertions is worse than 70% with strong ones, because it feels safe.",
    "continues": false,
    "checkpoints": [
      "100"
    ]
  },
  {
    "id": "m13-no-secret-in-the-repository-ever-and-if-one",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "No secret in the repository, ever — and if one gets in, rotate it.",
    "why": "Deleting it from the working tree leaves it in the history, and history gets cloned.",
    "continues": false
  },
  {
    "id": "m13-the-json-file-documents-the-shape-the",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "The JSON file documents the shape; the environment supplies the value.",
    "why": "Environment variables win over JSON, with `__` standing in for `:`.",
    "continues": false,
    "checkpoints": [
      "__"
    ]
  },
  {
    "id": "m13-validateonstart-on-everything-the-app-cannot",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "`ValidateOnStart` on everything the app cannot run without.",
    "why": "A misconfigured deploy should fail to start, loudly, not serve 500s an hour later.",
    "continues": false,
    "checkpoints": [
      "ValidateOnStart"
    ]
  },
  {
    "id": "m13-let-a-known-cve-fail-the-build",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Let a known CVE fail the build.",
    "why": "NuGetAudit plus warnings-as-errors plus central package management makes the fix a one-line diff.",
    "continues": false
  },
  {
    "id": "m13-newest-is-not-the-goal-patched-and",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Newest is not the goal. Patched and compatible is.",
    "why": "Read the advisory, and check what the framework package was built against before jumping a major version.",
    "continues": false
  },
  {
    "id": "m13-lock-the-restore-in-ci",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Lock the restore in CI.",
    "why": "A build that can silently pick up a version you never tested is not reproducible.",
    "continues": false
  },
  {
    "id": "m13-migrations-are-a-pipeline-step-not-a-startup",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Migrations are a pipeline step, not a startup step.",
    "why": "Instances race, and the application should not hold schema-altering permissions at runtime.",
    "continues": false
  },
  {
    "id": "m13-generate-the-script-with-idempotent-and-have",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Generate the script with `--idempotent`, and have a human read it.",
    "why": "EF sometimes decides a rename is a drop-and-recreate.",
    "continues": false,
    "checkpoints": [
      "--idempotent"
    ]
  },
  {
    "id": "m13-expand-migrate-contract",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Expand, migrate, contract.",
    "why": "Three deploys is the actual price of zero downtime — know that before you promise it.",
    "continues": false
  },
  {
    "id": "m13-aspnet-runtime-image-not-sdk-restore-before",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "`aspnet` runtime image, not `sdk`; restore before copying source; run as non-root.",
    "why": "100 MB instead of 800, a cache that actually works, and no compiler on a production host.",
    "continues": false,
    "checkpoints": [
      "aspnet",
      "sdk",
      "100"
    ]
  },
  {
    "id": "m13-liveness-dependency-free-readiness-not",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "Liveness dependency-free, readiness not.",
    "why": "Same rule as module 11, this time in the orchestrator's YAML.",
    "continues": false
  },
  {
    "id": "m13-an-untested-backup-is-a-hope-not-a-backup",
    "kind": "explain",
    "tier": "module",
    "module": "13",
    "part": "Module 13 — Configuration, security and deployment",
    "href": "course/module-13-deployment/",
    "claim": "An untested backup is a hope, not a backup.",
    "why": "Restore one before you need to.",
    "continues": false
  },
  {
    "id": "m14-measure-then-optimise",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Measure, then optimise.",
    "why": "A `Stopwatch` in a console app measures the JIT warming up; BenchmarkDotNet warms up, iterates, reports variance, and stops the JIT deleting your benchmark.",
    "continues": false,
    "checkpoints": [
      "Stopwatch"
    ]
  },
  {
    "id": "m14-allocated-usually-matters-more-than-mean",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "`Allocated` usually matters more than `Mean`.",
    "why": "A method 20 ns slower that allocates nothing beats a faster one that produces garbage, because GC pauses hit every request.",
    "continues": false,
    "checkpoints": [
      "Allocated",
      "Mean"
    ]
  },
  {
    "id": "m14-fix-the-algorithm-then-the-database-then",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Fix the algorithm, then the database, then allocation, and only then the microseconds.",
    "why": "Amdahl decides how much any of it is worth.",
    "continues": false
  },
  {
    "id": "m14-a-list-contains-inside-a-loop-is-o-n-m",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "A `List.Contains` inside a loop is O(n·m).",
    "why": "At 10,000 × 10,000 that is a hundred million comparisons a `HashSet` would not do.",
    "continues": false,
    "checkpoints": [
      "List.Contains",
      "HashSet"
    ]
  },
  {
    "id": "m14-short-lived-allocations-are-cheap-long-lived",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Short-lived allocations are cheap; long-lived ones are not.",
    "why": "Gen0 is nearly free; Gen2 pauses everything.",
    "continues": false
  },
  {
    "id": "m14-exceptions-cost-about-4-500-a-returned-value",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Exceptions cost about 4,500× a returned value here",
    "why": "— and the cost is in the throw, not the `try`. That is why business outcomes are `Result`s.",
    "continues": false,
    "checkpoints": [
      "try",
      "Result",
      "500"
    ]
  },
  {
    "id": "m14-span-t-only-in-a-measured-hot-path",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "`Span<T>` only in a measured hot path.",
    "why": "It cannot be a field, cannot be captured, cannot cross an `await`, and it makes the code harder to read.",
    "continues": false,
    "checkpoints": [
      "Span<T>",
      "await"
    ]
  },
  {
    "id": "m14-frozendictionary-for-data-that-never-changes",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "`FrozenDictionary` for data that never changes",
    "why": ", and never for anything you mutate.",
    "continues": true,
    "checkpoints": [
      "FrozenDictionary"
    ]
  },
  {
    "id": "m14-for-ef-core-in-this-order-asnotracking",
    "kind": "complete",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "For EF Core, in this order: `AsNoTracking`, project instead of loading, kill N+1, batch lookups, and only then compiled queries.",
    "stem": "For EF Core, in this order: `AsNoTracking`, project instead of",
    "why": "",
    "continues": false,
    "checkpoints": [
      "AsNoTracking"
    ]
  },
  {
    "id": "m14-read-the-sql-before-optimising-the-c",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Read the SQL before optimising the C#.",
    "why": "300 ms of database is not fixed by saving 20 ns in a loop.",
    "continues": false,
    "checkpoints": [
      "300"
    ]
  },
  {
    "id": "m14-profile-do-not-guess",
    "kind": "explain",
    "tier": "module",
    "module": "14",
    "part": "Module 14 — Performance",
    "href": "course/module-14-performance/",
    "claim": "Profile; do not guess.",
    "why": "`dotnet-counters`, `dotnet-trace`, the distributed trace, the query plan. Guessing is the step to skip.",
    "continues": false,
    "checkpoints": [
      "dotnet-counters",
      "dotnet-trace"
    ]
  },
  {
    "id": "m15-the-pipeline-is-one-nested-function-composed",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "The pipeline is one nested function, composed in reverse at build time.",
    "why": "Down on the way in, up on the way out, `await next()` is the hinge, and not calling it short-circuits.",
    "continues": false,
    "checkpoints": [
      "await next()"
    ]
  },
  {
    "id": "m15-order-is-behaviour",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Order is behaviour.",
    "why": "Exception handling first, rate limiting before authentication, authentication before authorization, forwarded headers before anything that reads the scheme or the client IP.",
    "continues": false
  },
  {
    "id": "m15-routing-matches-endpoints-execute",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Routing matches; endpoints execute; authorization lives in the gap.",
    "why": "Before `UseRouting`, `GetEndpoint()` is null and there is nothing to authorize against.",
    "continues": false,
    "checkpoints": [
      "UseRouting",
      "GetEndpoint()"
    ]
  },
  {
    "id": "m15-a-route-constraint-is-matching-not",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "A route constraint is matching, not validation.",
    "why": "`{id:guid}` given \"abc\" produces a 404, because no route matched — not a 400.",
    "continues": false,
    "checkpoints": [
      "{id:guid}",
      "404"
    ]
  },
  {
    "id": "m15-convention-based-middleware-is-constructed",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Convention-based middleware is constructed once.",
    "why": "Scoped services go in `InvokeAsync`'s parameters, never the constructor.",
    "continues": false,
    "checkpoints": [
      "InvokeAsync"
    ]
  },
  {
    "id": "m15-would-this-still-need-to-happen-if-the",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Would this still need to happen if the command came from a queue?",
    "why": "Yes ⇒ pipeline behaviour. No ⇒ middleware or endpoint filter.",
    "continues": false
  },
  {
    "id": "m15-a-non-nullable-parameter-is-a-required",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "A non-nullable parameter is a required parameter.",
    "why": "The binder never sees your property initialiser.",
    "continues": false
  },
  {
    "id": "m15-typedresults-over-results",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "`TypedResults` over `Results`.",
    "why": "The type carries the status and the payload, so a unit test can assert on it and OpenAPI can infer the shape.",
    "continues": false,
    "checkpoints": [
      "TypedResults",
      "Results"
    ]
  },
  {
    "id": "m15-ioptions-for-startup-values-ioptionssnapshot",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "`IOptions` for startup values, `IOptionsSnapshot` per request, `IOptionsMonitor` in singletons.",
    "why": "A `BackgroundService` taking `IOptionsSnapshot` is a captive dependency.",
    "continues": false,
    "checkpoints": [
      "IOptions",
      "IOptionsSnapshot",
      "IOptionsMonitor",
      "BackgroundService"
    ]
  },
  {
    "id": "m15-validateonstart-always",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "`ValidateOnStart()`, always.",
    "why": "Fail at deploy, not at the first login.",
    "continues": false,
    "checkpoints": [
      "ValidateOnStart()"
    ]
  },
  {
    "id": "m15-401-is-authentication-403-is-authorization",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "401 is authentication, 403 is authorization.",
    "why": "A valid-looking token returning 401 means authentication failed silently: check issuer, audience, signing key and clock skew, in that order.",
    "continues": false,
    "checkpoints": [
      "401",
      "403"
    ]
  },
  {
    "id": "m15-policies-at-the-call-site-roles-inside-the",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Policies at the call site, roles inside the policy.",
    "why": "Roles are data; policies are decisions, and a decision should live in one place.",
    "continues": false
  },
  {
    "id": "m15-behind-a-proxy-useforwardedheaders-first",
    "kind": "explain",
    "tier": "module",
    "module": "15",
    "part": "Module 15 — ASP.NET Core in depth",
    "href": "course/module-15-aspnetcore-in-depth/",
    "claim": "Behind a proxy, `UseForwardedHeaders` first",
    "why": "— or every client IP is the proxy's, and your per-user rate limiter partitions all anonymous traffic into one bucket.",
    "continues": false,
    "checkpoints": [
      "UseForwardedHeaders"
    ]
  },
  {
    "id": "m16-they-stack-they-do-not-compete",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "They stack, they do not compete.",
    "why": "C# is the language, SQL is a different language run by a different program, LINQ is a query API that can run either side, ASP.NET Core is the edge.",
    "continues": false
  },
  {
    "id": "m16-push-the-work-to-where-the-data-is-unless",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "Push the work to where the data is — unless the data is already here.",
    "why": "Filtering 4,000,000 rows in C# is the single most common performance mistake in this field.",
    "continues": false
  },
  {
    "id": "m16-the-border-is-the-last-iqueryable",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "The border is the last `IQueryable`.",
    "why": "`AsEnumerable()`, an `IEnumerable<T>` variable, or a method signature returning one, moves everything after it into your process.",
    "continues": false,
    "checkpoints": [
      "IQueryable",
      "AsEnumerable()",
      "IEnumerable<T>"
    ]
  },
  {
    "id": "m16-a-guarantee-belongs-at-the-lowest-layer-that",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "A guarantee belongs at the lowest layer that can actually enforce it.",
    "why": "A C# uniqueness check is a nicer error message; the unique index is the guarantee. Keep both, and know which is which.",
    "continues": false
  },
  {
    "id": "m16-only-the-database-can-arbitrate-between",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "Only the database can arbitrate between processes.",
    "why": "Two API instances cannot see each other, so \"do not oversell the last unit\" is `rowversion` and a constraint, not an `if`.",
    "continues": false,
    "checkpoints": [
      "rowversion",
      "if"
    ]
  },
  {
    "id": "m16-ef-core-throws-rather-than-falling-back",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "EF Core throws rather than falling back — except in the final `Select`.",
    "why": "Client evaluation in a projection does not change how much data crosses the wire; in a `Where` it changes everything.",
    "continues": false,
    "checkpoints": [
      "Select",
      "Where"
    ]
  },
  {
    "id": "m16-c-has-two-truth-values-sql-has-three",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "C# has two truth values; SQL has three.",
    "why": "Those `OR … IS NULL` clauses are EF Core preserving C# semantics across the border, and they can cost you an index seek.",
    "continues": false,
    "checkpoints": [
      "OR … IS NULL"
    ]
  },
  {
    "id": "m16-the-database-is-probably-case-insensitive",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "The database is probably case-insensitive and your `List<T>` is not.",
    "why": "The same predicate gives two different answers on either side of the line — which is an argument against the in-memory provider all by itself.",
    "continues": false,
    "checkpoints": [
      "List<T>"
    ]
  },
  {
    "id": "m16-no-order-by-means-no-order",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "No `ORDER BY` means no order.",
    "why": "Not insertion order, not primary-key order. And SQL's ordering is not stable, so paging needs a unique tiebreaker.",
    "continues": false,
    "checkpoints": [
      "ORDER BY"
    ]
  },
  {
    "id": "m16-money-is-decimal-never-double",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "Money is `decimal`, never `double`",
    "why": ", on both sides of the border.",
    "continues": true,
    "checkpoints": [
      "decimal",
      "double"
    ]
  },
  {
    "id": "m16-only-one-file-in-the-solution-should-know",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "Only one file in the solution should know what a 409 is.",
    "why": "The domain classifies the error; the edge maps the classification to a status code. That is what lets a message queue drive the same domain tomorrow.",
    "continues": false,
    "checkpoints": [
      "409"
    ]
  },
  {
    "id": "m16-when-something-is-wrong-name-the-layer-first",
    "kind": "explain",
    "tier": "module",
    "module": "16",
    "part": "Module 16 — The layer map: C#, LINQ, SQL and ASP.NET Core",
    "href": "course/module-16-the-layer-map/",
    "claim": "When something is wrong, name the layer first.",
    "why": "The diagnostic table in §8 turns \"it is broken\" into \"it is the border, so look at collation, nulls, precision and ordering\".",
    "continues": false
  },
  {
    "id": "m17-search-the-whole-modena-bologna-corridor",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Search the whole Modena–Bologna corridor",
    "why": ", not one city. It is one labour market and it is forty minutes wide.",
    "continues": true
  },
  {
    "id": "m17-lead-with-sql",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Lead with SQL.",
    "why": "Almost nobody at junior or mid level can explain why a query is slow or read an execution plan. Module 07 puts you in a small minority — say so early.",
    "continues": false
  },
  {
    "id": "m17-apply-at-about-60-match",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Apply at about 60% match.",
    "why": "Italian job adverts list a wish, not a filter.",
    "continues": false
  },
  {
    "id": "m17-applications-start-in-week-5-not-week-12",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Applications start in week 5, not week 12.",
    "why": "Pipelines take weeks; the technical work and the search run in parallel or you finish the course unemployed.",
    "continues": false
  },
  {
    "id": "m17-put-your-italian-on-the-cv-as-a-trajectory",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Put your Italian on the CV as a trajectory.",
    "why": "\"Italiano: A2, in studio attivo\" beats silence, and answers the question every recruiter is silently asking.",
    "continues": false
  },
  {
    "id": "m17-state-your-work-authorisation-in-one-line",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "State your work authorisation in one line.",
    "why": "Recruiters discard ambiguity rather than investigate it.",
    "continues": false
  },
  {
    "id": "m17-two-pages-pdf-the-gdpr-line-no-europass",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Two pages, PDF, the GDPR line, no Europass, project before education.",
    "why": "For a career changer, a public repository with a real architecture beats almost everything else on the page.",
    "continues": false
  },
  {
    "id": "m17-re-skin-logiflow-to-the-local-domain-before",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Re-skin LogiFlow to the local domain before you send it",
    "why": "— production orders, work centres, lot traceability. Mostly renaming, and it changes what you are in the room.",
    "continues": false
  },
  {
    "id": "m17-be-able-to-defend-every-decision-in-it",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Be able to defend every decision in it, including the compromises the README admits to.",
    "why": "Naming the condition under which you would *not* do something is what reads as senior.",
    "continues": false
  },
  {
    "id": "m17-prefer-employment-to-partita-iva-for-a-first",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Prefer employment to `partita IVA` for a first job here.",
    "why": "Fixed hours at one client's office with their equipment is not freelancing; it is the burden without the protection.",
    "continues": false,
    "checkpoints": [
      "partita IVA"
    ]
  },
  {
    "id": "m17-ask-about-trasferta-the-legacy-estate-who",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Ask about trasferta, the legacy estate, who owns the database, and which CCNL.",
    "why": "Their answers tell you whether you want the job; the questions tell them you have worked on real systems.",
    "continues": false
  },
  {
    "id": "m17-compare-offers-on-ral-contract-type-ccnl",
    "kind": "explain",
    "tier": "module",
    "module": "17",
    "part": "Module 17 — Landing a .NET job in Modena and Bologna",
    "href": "course/module-17-career-emilia-romagna/",
    "claim": "Compare offers on RAL, contract type, CCNL, meal vouchers and travel — not on the number alone.",
    "why": "And verify every figure in this module before you negotiate: they move, and I cannot check them for you.",
    "continues": false
  },
  {
    "id": "m18-choose-the-render-mode-on-users-network-and",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Choose the render mode on users, network and secrets",
    "why": "— not on preference. Server: fast first paint, no download, the token stays server-side, one stateful circuit per user. WebAssembly: heavy first load, no server state, scales like static files.",
    "continues": false
  },
  {
    "id": "m18-in-blazor-server-component-state-is-server",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "In Blazor Server, component state is server memory.",
    "why": "Paging a large list is a capacity decision, not a nicety.",
    "continues": false
  },
  {
    "id": "m18-errorboundary-is-not-optional-and-it-latches",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "`ErrorBoundary` is not optional, and it latches.",
    "why": "Until you call `Recover()`, the error message never goes away.",
    "continues": false,
    "checkpoints": [
      "ErrorBoundary",
      "Recover()"
    ]
  },
  {
    "id": "m18-do-not-throw-for-expected-failures",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Do not throw for expected failures.",
    "why": "A 404 or a 409 is an ordinary outcome; here an exception costs the user their session.",
    "continues": false,
    "checkpoints": [
      "404",
      "409"
    ]
  },
  {
    "id": "m18-a-typed-client-and-a-delegatinghandler-for",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "A typed client, and a `DelegatingHandler` for the token.",
    "why": "One place owns the base address, the auth header, the JSON options and the error translation — including for the code someone adds next year.",
    "continues": false,
    "checkpoints": [
      "DelegatingHandler"
    ]
  },
  {
    "id": "m18-duplicate-the-contract-across-a-published",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Duplicate the contract across a published boundary.",
    "why": "A rename in the Application layer *should* break the client; if the UI simply is the server's types, there is no contract, only coupling. And write enum values out explicitly when they travel as integers.",
    "continues": false
  },
  {
    "id": "m18-let-the-server-own-the-state-machine",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Let the server own the state machine.",
    "why": "Render the buttons from `AllowedTransitions`; the moment the UI reimplements the transition table, the two start drifting.",
    "continues": false,
    "checkpoints": [
      "AllowedTransitions"
    ]
  },
  {
    "id": "m18-oninitializedasync-runs-twice-on-first-load",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "`OnInitializedAsync` runs twice on first load and never again on a route change.",
    "why": "Prerender plus interactive is two fetches; navigation between two URLs on the same `@page` reuses the instance. Load data in `OnParametersSetAsync`.",
    "continues": false,
    "checkpoints": [
      "OnInitializedAsync",
      "@page",
      "OnParametersSetAsync"
    ]
  },
  {
    "id": "m18-key-every-repeated-element",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "`@key` every repeated element.",
    "why": "Without it the diff matches by position, and state inside a row — input text, focus, a checked box — follows the position instead of the record.",
    "continues": false,
    "checkpoints": [
      "@key"
    ]
  },
  {
    "id": "m18-debounce-with-an-awaited-task-delay-and-a",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Debounce with an awaited `Task.Delay` and a token, not a `Timer`.",
    "why": "The continuation resumes on the renderer's synchronisation context; a timer callback does not, and that is where the race lives. Anything touching state from off that context needs `InvokeAsync(StateHasChanged)`.",
    "continues": false,
    "checkpoints": [
      "Task.Delay",
      "Timer",
      "InvokeAsync(StateHasChanged)"
    ]
  },
  {
    "id": "m18-two-cancellation-sources-one-for-the",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Two cancellation sources: one for the keystroke, one for the request in flight.",
    "why": "Share them and the next keystroke cancels the request it was waiting for.",
    "continues": false
  },
  {
    "id": "m18-fire-and-forget-needs-the-trio-a-discard-a",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Fire-and-forget needs the trio: a discard, a token, and a `Dispose` that cancels it.",
    "why": "Otherwise navigating away leaves a timer holding a disposed component.",
    "continues": false,
    "checkpoints": [
      "Dispose"
    ]
  },
  {
    "id": "m18-format-anything-that-becomes-css-with",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Format anything that becomes CSS with `InvariantCulture`.",
    "why": "On an Italian machine `width:33,33%` is silently dropped by the browser and nothing anywhere mentions culture.",
    "continues": false,
    "checkpoints": [
      "InvariantCulture",
      "width:33,33%"
    ]
  },
  {
    "id": "m18-reload-after-a-409-and-keep-the-error",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Reload after a 409 and keep the error visible.",
    "why": "A UI showing only the error leaves the user staring at a screen that disagrees with the database.",
    "continues": false,
    "checkpoints": [
      "409"
    ]
  },
  {
    "id": "m18-respect-prefers-reduced-motion-by-shortening",
    "kind": "explain",
    "tier": "module",
    "module": "18",
    "part": "Module 18 — Blazor: a UI over the API",
    "href": "course/module-18-blazor/",
    "claim": "Respect `prefers-reduced-motion` by shortening durations, not removing animations",
    "why": "— an element animated with `both` that never runs is an element that never appears.",
    "continues": false,
    "checkpoints": [
      "prefers-reduced-motion",
      "both"
    ]
  },
  {
    "id": "m19-allocation-is-a-pointer-bump-collection-is",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "Allocation is a pointer bump; collection is what costs — and it costs in proportion to SURVIVORS, not to garbage.",
    "why": "A million short-lived objects are cheap. Ten thousand long-lived ones are not.",
    "continues": false
  },
  {
    "id": "m19-a-leak-in-net-is-a-reference-you-forgot-not",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "A leak in .NET is a reference you forgot, not memory you forgot to free.",
    "why": "The first suspects are an unsubscribed event, a static collection, and a cache with no eviction.",
    "continues": false
  },
  {
    "id": "m19-a-value-type-lives-wherever-its-container",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "A value type lives wherever its container lives.",
    "why": "\"Structs are on the stack\" is wrong: a struct field of a class is on the heap, and a captured local is on the heap too.",
    "continues": false
  },
  {
    "id": "m19-85-000-bytes-is-the-large-object-heap-line",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "85,000 bytes is the Large Object Heap line.",
    "why": "Above it, an object is born in gen 2 and is not compacted. Pool the buffer instead of crossing it.",
    "continues": false
  },
  {
    "id": "m19-gen-0-is-cheap-because-of-the-write-barrier",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "Gen 0 is cheap because of the write barrier and the card table",
    "why": "— old-to-young references are recorded when written, so a nursery collection never has to scan the old heap.",
    "continues": false
  },
  {
    "id": "m19-a-finalizer-is-a-safety-net-for-unmanaged",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "A finalizer is a safety net for unmanaged memory, and nothing else.",
    "why": "It costs an extra generation of lifetime, runs on someone else's thread at an unknown time, and may never run.",
    "continues": false
  },
  {
    "id": "m19-if-you-own-something-disposable-you-are",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "If you own something disposable, you are disposable — and if you did not create it, do not dispose it.",
    "why": "Both halves cause outages.",
    "continues": false
  },
  {
    "id": "m19-dispose-must-be-idempotent-and-must-not",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "`Dispose` must be idempotent and must not throw.",
    "why": "It runs on the exception path, where throwing hides the real failure.",
    "continues": false,
    "checkpoints": [
      "Dispose"
    ]
  },
  {
    "id": "m19-await-using-whenever-the-type-offers",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "`await using` whenever the type offers `IAsyncDisposable`.",
    "why": "A plain `using` on a type that implements both silently picks the blocking one.",
    "continues": false,
    "checkpoints": [
      "await using",
      "IAsyncDisposable",
      "using"
    ]
  },
  {
    "id": "m19-a-rented-buffer-is-dirty-and-is-bigger-than",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "A rented buffer is dirty and is bigger than you asked for.",
    "why": "Never trust `.Length`, always `Return` in a `finally`, and clear it if it held anything private.",
    "continues": false,
    "checkpoints": [
      ".Length",
      "Return",
      "finally"
    ]
  },
  {
    "id": "m19-never-call-gc-collect-in-production",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "Never call `GC.Collect()` in production.",
    "why": "It forces a full blocking collection and promotes every survivor — the exact opposite of what you wanted.",
    "continues": false,
    "checkpoints": [
      "GC.Collect()"
    ]
  },
  {
    "id": "m19-server-gc-for-servers-and-give-the-container",
    "kind": "explain",
    "tier": "module",
    "module": "19",
    "part": "Module 19 — Memory, the heap, and the garbage collector",
    "href": "course/module-19-memory-and-gc/",
    "claim": "Server GC for servers, and give the container a memory limit the runtime can see.",
    "why": "One heap per core is throughput; one heap per core inside an unaware 512 MB container is an OOM kill.",
    "continues": false,
    "checkpoints": [
      "512"
    ]
  },
  {
    "id": "m20-is-static-equals-is-virtual",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "`==` is static, `Equals` is virtual.",
    "why": "The compiler picks `==` from the declared type; the runtime picks `Equals` from the actual one. In generic code with an unconstrained `T`, `==` is reference equality — use `EqualityComparer<T>.Default`.",
    "continues": false,
    "checkpoints": [
      "==",
      "Equals",
      "is reference equality — use"
    ]
  },
  {
    "id": "m20-override-equals-and-you-must-override",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "Override `Equals` and you must override `GetHashCode`.",
    "why": "Equal objects must hash equally, or every hash-based collection quietly loses your data.",
    "continues": false,
    "checkpoints": [
      "Equals",
      "GetHashCode"
    ]
  },
  {
    "id": "m20-the-hash-of-a-key-must-never-change-while-it",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "The hash of a key must never change while it is in the table.",
    "why": "Mutate it and the entry becomes unreachable and unremovable — so dictionary keys are immutable, full stop.",
    "continues": false
  },
  {
    "id": "m20-hashcode-combine-never-xor-and-never-sum",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "`HashCode.Combine`, never XOR and never sum.",
    "why": "XOR is commutative, so `(1,2)` and `(2,1)` collide, and in a composite key that is half your rows in one bucket.",
    "continues": false,
    "checkpoints": [
      "HashCode.Combine",
      "(1,2)",
      "(2,1)"
    ]
  },
  {
    "id": "m20-a-gethashcode-value-is-valid-for-one-process",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "A `GetHashCode()` value is valid for one process, for one run.",
    "why": "The string seed is randomised per process. Never persist it, send it, or shard on it.",
    "continues": false,
    "checkpoints": [
      "GetHashCode()"
    ]
  },
  {
    "id": "m20-a-struct-used-as-a-dictionary-key-must",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "A struct used as a dictionary key must implement `IEquatable<T>`",
    "why": "— otherwise every lookup boxes and falls back to a reflection-driven comparison. `readonly record struct` gives you both for free.",
    "continues": false,
    "checkpoints": [
      "IEquatable<T>",
      "readonly record struct"
    ]
  },
  {
    "id": "m20-if-compareto-returns-0-equals-must-return",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "If `CompareTo` returns 0, `Equals` must return true.",
    "why": "Sorted collections use only the first, hashed collections only the second; letting them disagree gives you two truths.",
    "continues": false,
    "checkpoints": [
      "CompareTo",
      "Equals"
    ]
  },
  {
    "id": "m20-never-write-a-value-b-value-in-a-comparer",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "Never write `a.Value - b.Value` in a comparer.",
    "why": "It overflows and reverses the sign. `CompareTo`, always.",
    "continues": false,
    "checkpoints": [
      "a.Value - b.Value",
      "CompareTo"
    ]
  },
  {
    "id": "m20-contains-in-a-loop-over-a-list-t-is-o-n",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "`Contains` in a loop over a `List<T>` is O(n²).",
    "why": "Build a `HashSet<T>` first. This is the most common real performance bug in ordinary business code.",
    "continues": false,
    "checkpoints": [
      "Contains",
      "List<T>",
      "HashSet<T>"
    ]
  },
  {
    "id": "m20-size-a-dictionary-you-are-about-to-fill",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "Size a dictionary you are about to fill.",
    "why": "Growth rehashes every entry, and the constructor takes the capacity.",
    "continues": false
  },
  {
    "id": "m20-trygetvalue-and-tryadd-are-one-lookup",
    "kind": "complete",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "`TryGetValue` and `TryAdd` are one lookup; `ContainsKey` plus an indexer is two.",
    "stem": "`TryGetValue` and `TryAdd` are one lookup;",
    "why": "",
    "continues": false,
    "checkpoints": [
      "TryGetValue",
      "TryAdd",
      "ContainsKey"
    ]
  },
  {
    "id": "m20-dictionary-enumeration-order-is-undefined",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "Dictionary enumeration order is undefined.",
    "why": "It looks like insertion order until the first removal, and then it does not.",
    "continues": false
  },
  {
    "id": "m20-concurrentdictionary-makes-each-operation",
    "kind": "explain",
    "tier": "module",
    "module": "20",
    "part": "Module 20 — Equality, hashing, and choosing a collection",
    "href": "course/module-20-equality-and-collections/",
    "claim": "`ConcurrentDictionary` makes each operation atomic, not each sequence",
    "why": "— and `GetOrAdd` may invoke your factory more than once, so the factory must be cheap and side-effect free.",
    "continues": false,
    "checkpoints": [
      "ConcurrentDictionary",
      "GetOrAdd"
    ]
  },
  {
    "id": "m21-a-task-is-not-a-thread-and-a-thread-is-not-a",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "A task is not a thread, and a thread is not a core.",
    "why": "Tasks are cheap promises; threads cost about a megabyte and a millisecond; cores are the only real parallelism you have.",
    "continues": false
  },
  {
    "id": "m21-the-pool-grows-by-roughly-one-thread-per",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "The pool grows by roughly one thread per second above the core count.",
    "why": "That single number is the entire mechanism of thread-pool starvation.",
    "continues": false
  },
  {
    "id": "m21-p99-latency-climbing-while-cpu-sits-idle",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "p99 latency climbing while CPU sits idle means threads, not compute.",
    "why": "The cause is always blocking: `.Result`, `.Wait()`, a synchronous I/O call, or lock contention.",
    "continues": false,
    "checkpoints": [
      ".Result",
      ".Wait()"
    ]
  },
  {
    "id": "m21-counter-is-read-add-write-not-one-operation",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`counter++` is read-add-write, not one operation.",
    "why": "Two threads lose updates immediately, and at volume they lose most of them.",
    "continues": false,
    "checkpoints": [
      "counter++"
    ]
  },
  {
    "id": "m21-interlocked-for-one-variable-lock-for-an",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`Interlocked` for one variable, `lock` for an invariant across several, a channel for not sharing at all.",
    "why": "In that order of preference.",
    "continues": false,
    "checkpoints": [
      "Interlocked",
      "lock"
    ]
  },
  {
    "id": "m21-interlocked-compareexchange-is-optimistic",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`Interlocked.CompareExchange` is optimistic concurrency at the CPU level",
    "why": "— the same read/compute/conditional-write/retry shape as a `rowversion` check.",
    "continues": false,
    "checkpoints": [
      "Interlocked.CompareExchange",
      "rowversion"
    ]
  },
  {
    "id": "m21-lock-on-a-private-readonly-object-never-on",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "Lock on a private readonly object, never on `this`, a `Type`, or a string.",
    "why": "Anything a stranger can lock on is a deadlock written in someone else's file.",
    "continues": false,
    "checkpoints": [
      "this",
      "Type"
    ]
  },
  {
    "id": "m21-never-await-inside-a-lock",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "Never `await` inside a lock.",
    "why": "A monitor belongs to a thread; a continuation may resume on another. `SemaphoreSlim.WaitAsync` is the async critical section.",
    "continues": false,
    "checkpoints": [
      "await",
      "SemaphoreSlim.WaitAsync"
    ]
  },
  {
    "id": "m21-take-multiple-locks-in-one-globally",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "Take multiple locks in one globally consistent order",
    "why": ", and never call unknown code while holding one.",
    "continues": true
  },
  {
    "id": "m21-without-a-fence-another-thread-may-never-see",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "Without a fence, another thread may never see your write.",
    "why": "The compiler may hoist it, the CPU may reorder it. `lock`, `Interlocked` and `volatile` are the fences.",
    "continues": false,
    "checkpoints": [
      "lock",
      "Interlocked",
      "volatile"
    ]
  },
  {
    "id": "m21-volatile-does-not-make-atomic",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`volatile` does not make `++` atomic.",
    "why": "It orders individual reads and writes; the read-modify-write in between is still three steps.",
    "continues": false,
    "checkpoints": [
      "volatile",
      "++"
    ]
  },
  {
    "id": "m21-long-and-double-are-not-atomic-on-32-bit-and",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`long` and `double` are not atomic on 32-bit, and no multi-word struct ever is.",
    "why": "A torn read returns a value that never existed.",
    "continues": false,
    "checkpoints": [
      "long",
      "double"
    ]
  },
  {
    "id": "m21-parallel-plinq-are-for-cpu-bound-work-only",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`Parallel`/PLINQ are for CPU-bound work only",
    "why": ", the body must be thread-safe, and below a few thousand items the partitioning costs more than it saves.",
    "continues": true,
    "checkpoints": [
      "Parallel"
    ]
  },
  {
    "id": "m21-task-whenall-throws-only-the-first-exception",
    "kind": "explain",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "`Task.WhenAll` throws only the first exception.",
    "why": "The rest are on the task's `Exception` property, silent unless you look.",
    "continues": false,
    "checkpoints": [
      "Task.WhenAll",
      "Exception"
    ]
  },
  {
    "id": "m21-asp-net-core-has-no-synchronization-context",
    "kind": "complete",
    "tier": "module",
    "module": "21",
    "part": "Module 21 — Threading, locks, and the memory model",
    "href": "course/module-21-threading-and-memory-model/",
    "claim": "ASP.NET Core has no synchronization context, so sync-over-async passes every test and deadlocks in Blazor Server and WPF.",
    "stem": "ASP.NET Core has no synchronization context,",
    "why": "",
    "continues": false
  },
  {
    "id": "m22-a-dll-is-il-plus-metadata-not-machine-code",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "A .dll is IL plus metadata, not machine code.",
    "why": "That is why reflection, decompilers and source generators are possible, and why shipping a build is not obfuscation.",
    "continues": false
  },
  {
    "id": "m22-the-jit-compiles-per-method-on-first-call",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "The JIT compiles per method, on first call.",
    "why": "First-request latency and misleading console timings both come from this.",
    "continues": false
  },
  {
    "id": "m22-tier-0-gets-you-started-tier-1-gets-you-fast",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Tier 0 gets you started, tier 1 gets you fast, OSR bridges a long-running loop.",
    "why": "You do not configure it; you just stop being surprised by it.",
    "continues": false
  },
  {
    "id": "m22-readytorun-for-faster-startup-with-no-loss",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "ReadyToRun for faster startup with no loss of peak speed; Native AOT when you can give up runtime code generation.",
    "why": "No `Expression.Compile`, no `Reflection.Emit`, and reflection over trimmed types throws.",
    "continues": false,
    "checkpoints": [
      "Expression.Compile",
      "Reflection.Emit"
    ]
  },
  {
    "id": "m22-every-object-carries-a-sync-block-index-and",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Every object carries a sync-block index and a method-table pointer.",
    "why": "That is `GetType()`, virtual dispatch, `lock`, and boxing, all from one 16-byte header.",
    "continues": false,
    "checkpoints": [
      "GetType()",
      "lock"
    ]
  },
  {
    "id": "m22-generics-are-instantiated-at-run-time-shared",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Generics are instantiated at run time: shared code for reference types, specialised code per value type.",
    "why": "Type safety and no boxing, at the cost of JIT time per struct instantiation.",
    "continues": false
  },
  {
    "id": "m22-a-static-field-in-a-generic-type-is-per",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "A static field in a generic type is per closed type.",
    "why": "`Cache<int>` and `Cache<string>` are two caches — a feature, and a foot-gun if you did not intend it.",
    "continues": false,
    "checkpoints": [
      "Cache<int>",
      "Cache<string>"
    ]
  },
  {
    "id": "m22-a-generic-constraint-removes-the-boxing-an",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "A generic constraint removes the boxing an interface parameter forces.",
    "why": "Same method body, 240,000 bytes versus zero.",
    "continues": false,
    "checkpoints": [
      "240"
    ]
  },
  {
    "id": "m22-reflection-is-slow-because-of-the-metadata",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Reflection is slow because of the metadata lookup and the boxing, not because it is reflection.",
    "why": "Cache the `MemberInfo`, build a delegate, and the cost is gone.",
    "continues": false,
    "checkpoints": [
      "MemberInfo"
    ]
  },
  {
    "id": "m22-prefer-a-source-generator-to-run-time",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Prefer a source generator to run-time reflection.",
    "why": "Compile-time work is faster, debuggable, trimming-safe, and turns run-time failures into build errors.",
    "continues": false
  },
  {
    "id": "m22-pinning-fragments-the-heap",
    "kind": "explain",
    "tier": "module",
    "module": "22",
    "part": "Module 22 — Inside the CLR: compilation, types, and the JIT",
    "href": "course/module-22-clr-internals/",
    "claim": "Pinning fragments the heap.",
    "why": "`fixed` stops the GC moving an object; `Span<T>` is the safe answer for nearly every case that used to need it.",
    "continues": false,
    "checkpoints": [
      "fixed",
      "Span<T>"
    ]
  },
  {
    "id": "m23-string-is-immutable-so-every-concatenation",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "`string` is immutable, so every concatenation allocates.",
    "why": "Fine for three; O(n²) in a loop. `StringBuilder`, or `string.Create`.",
    "continues": false,
    "checkpoints": [
      "string",
      "StringBuilder",
      "string.Create"
    ]
  },
  {
    "id": "m23-a-char-is-a-utf-16-code-unit-not-a-character",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "A `char` is a UTF-16 code unit, not a character.",
    "why": "An emoji has `Length == 2`. Never reverse or truncate by `char` index, and normalise before comparing text from different sources.",
    "continues": false,
    "checkpoints": [
      "char",
      "Length == 2"
    ]
  },
  {
    "id": "m23-ordinal-for-identifiers-invariant-for",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Ordinal for identifiers, Invariant for persistence, Current for humans.",
    "why": "Three categories, no fourth, and every call site belongs to exactly one.",
    "continues": false
  },
  {
    "id": "m23-an-unqualified-tostring-parse-is-a-latent",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "An unqualified `ToString()`/`Parse` is a latent bug on a non-English machine.",
    "why": "On `it-IT`, `\"1234.5\"` parses to `12345`, silently.",
    "continues": false,
    "checkpoints": [
      "ToString()",
      "Parse",
      "it-IT",
      "\"1234.5\"",
      "12345"
    ]
  },
  {
    "id": "m23-tolower-without-a-culture-fails-in-turkish",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "`ToLower()` without a culture fails in Turkish.",
    "why": "The dotless `ı` breaks the oldest string comparison in the book. Use `OrdinalIgnoreCase` rather than case-folding at all.",
    "continues": false,
    "checkpoints": [
      "ToLower()",
      "OrdinalIgnoreCase"
    ]
  },
  {
    "id": "m23-anything-that-becomes-css-json-a-url-or-sql",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Anything that becomes CSS, JSON, a URL or SQL is formatted with `InvariantCulture`.",
    "why": "`width:33,33%` is silently dropped by every browser.",
    "continues": false,
    "checkpoints": [
      "InvariantCulture",
      "width:33,33%"
    ]
  },
  {
    "id": "m23-ordinal-is-both-the-correct-answer-and-the",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Ordinal is both the correct answer and the fast one",
    "why": "for identifiers — it is a memory compare, not a collation walk.",
    "continues": false
  },
  {
    "id": "m23-store-utc-transmit-iso-8601-with-an-offset",
    "kind": "complete",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Store UTC, transmit ISO-8601 with an offset, convert only at the edge.",
    "stem": "Store UTC, transmit ISO-8601 with an",
    "why": "",
    "continues": false
  },
  {
    "id": "m23-datetime-kind-is-not-persisted-by-most",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "`DateTime.Kind` is not persisted by most databases",
    "why": ", so a Utc value comes back Unspecified and shifts on the next conversion. Use `DateTimeOffset` for an instant.",
    "continues": true,
    "checkpoints": [
      "DateTime.Kind",
      "DateTimeOffset"
    ]
  },
  {
    "id": "m23-an-offset-is-not-a-time-zone",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "An offset is not a time zone.",
    "why": "Only `TimeZoneInfo` knows that 02:30 does not exist one night in March and happens twice one night in October.",
    "continues": false,
    "checkpoints": [
      "TimeZoneInfo"
    ]
  },
  {
    "id": "m23-inject-timeprovider",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Inject `TimeProvider`.",
    "why": "`DateTime.UtcNow` is a hidden static dependency, and it is why month-end and expiry logic cannot be tested.",
    "continues": false,
    "checkpoints": [
      "TimeProvider",
      "DateTime.UtcNow"
    ]
  },
  {
    "id": "m23-jsonserializer-serializes-the-declared-type",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "`JsonSerializer` serializes the DECLARED type.",
    "why": "A derived object assigned to a base-typed variable silently loses its extra properties. `[JsonDerivedType]`, or serialize `n.GetType()`.",
    "continues": false,
    "checkpoints": [
      "JsonSerializer",
      "[JsonDerivedType]",
      "n.GetType()"
    ]
  },
  {
    "id": "m23-register-one-jsonserializeroptions-and-reuse",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Register one `JsonSerializerOptions` and reuse it.",
    "why": "It caches per-type metadata; a new instance per call throws that away, and mismatched instances give you two casing conventions.",
    "continues": false,
    "checkpoints": [
      "JsonSerializerOptions"
    ]
  },
  {
    "id": "m23-serialize-enums-as-strings-across-a",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Serialize enums as strings across a published boundary",
    "why": "— and pin the numeric values anyway.",
    "continues": false
  },
  {
    "id": "m23-deserializing-to-object-yields-a-jsonelement",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Deserializing to `object` yields a `JsonElement` over a buffer",
    "why": "that throws once the document is disposed.",
    "continues": false,
    "checkpoints": [
      "object",
      "JsonElement"
    ]
  },
  {
    "id": "m23-use-json-source-generation",
    "kind": "explain",
    "tier": "module",
    "module": "23",
    "part": "Module 23 — Text, culture, time, and serialization",
    "href": "course/module-23-text-culture-serialization/",
    "claim": "Use JSON source generation.",
    "why": "Compile-time, no reflection, trimming- and AOT-safe.",
    "continues": false
  },
  {
    "id": "m24-useauthentication-before-useauthorization",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "`UseAuthentication` before `UseAuthorization`.",
    "why": "Reversed, `User` is empty when the policy runs — every protected endpoint 401s and it looks like a token bug.",
    "continues": false,
    "checkpoints": [
      "UseAuthentication",
      "UseAuthorization",
      "User"
    ]
  },
  {
    "id": "m24-a-jwt-is-signed-not-encrypted",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "A JWT is signed, not encrypted.",
    "why": "Anyone holding it can read every claim, so nothing private goes in a token.",
    "continues": false
  },
  {
    "id": "m24-validate-issuer-audience-lifetime-signature",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Validate issuer, audience, lifetime, signature and algorithm.",
    "why": "Never trust the `alg` the token names.",
    "continues": false,
    "checkpoints": [
      "alg"
    ]
  },
  {
    "id": "m24-a-jwt-cannot-be-revoked-so-keep-it-short",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "A JWT cannot be revoked, so keep it short-lived",
    "why": "and put the revocable state in a refresh token you store server-side.",
    "continues": false
  },
  {
    "id": "m24-use-asymmetric-signing-beyond-a-single",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Use asymmetric signing beyond a single service.",
    "why": "With HMAC, everything that can verify can also mint.",
    "continues": false
  },
  {
    "id": "m24-named-policies-at-the-endpoint-never-inline",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Named policies at the endpoint, never inline role lists.",
    "why": "The rule is then defined once and can change shape without touching the endpoints.",
    "continues": false
  },
  {
    "id": "m24-hash-passwords-with-a-slow-salted-kdf",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Hash passwords with a slow, salted KDF",
    "why": "— Argon2id or PBKDF2 with a high iteration count. A fast hash is the attacker's dream, so SHA-256 alone is a mistake.",
    "continues": false,
    "checkpoints": [
      "256"
    ]
  },
  {
    "id": "m24-compare-secrets-in-constant-time",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Compare secrets in constant time",
    "why": ", and return the same message and timing for \"no such user\" as for \"wrong password\".",
    "continues": true
  },
  {
    "id": "m24-parameterise-every-query",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Parameterise every query.",
    "why": "The value is transmitted separately from the SQL text, so its content can never change the statement's structure. Escaping is not the same thing.",
    "continues": false
  },
  {
    "id": "m24-a-column-or-table-name-cannot-be-a-parameter",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "A column or table name cannot be a parameter — use an allow list.",
    "why": "Dynamic `ORDER BY` is where injection survives in an EF codebase.",
    "continues": false,
    "checkpoints": [
      "ORDER BY"
    ]
  },
  {
    "id": "m24-bind-to-a-request-dto-never-to-an-entity",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Bind to a request DTO, never to an entity.",
    "why": "Over-posting becomes structurally impossible rather than merely unlikely.",
    "continues": false
  },
  {
    "id": "m24-object-level-authorization-belongs-in-the",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Object-level authorization belongs in the handler, next to the data.",
    "why": "An endpoint policy cannot express \"may this user see *this* row\", and IDOR is the vulnerability scanners miss.",
    "continues": false
  },
  {
    "id": "m24-every-list-endpoint-has-a-maximum-page-size",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Every list endpoint has a maximum page size.",
    "why": "An unbounded one is a denial-of-service parameter with a friendly name.",
    "continues": false
  },
  {
    "id": "m24-return-a-problemdetails-with-a-correlation",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Return a `ProblemDetails` with a correlation id; log the detail server-side.",
    "why": "A stack trace in a response is reconnaissance.",
    "continues": false,
    "checkpoints": [
      "ProblemDetails"
    ]
  },
  {
    "id": "m24-cors-is-a-browser-policy-not-a-security",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "CORS is a browser policy, not a security boundary.",
    "why": "It does nothing against a non-browser client.",
    "continues": false
  },
  {
    "id": "m24-anti-forgery-is-for-cookie-auth-not-bearer",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Anti-forgery is for cookie auth, not bearer tokens",
    "why": "— because the browser attaches cookies for you and does not attach an `Authorization` header.",
    "continues": false,
    "checkpoints": [
      "Authorization"
    ]
  },
  {
    "id": "m24-a-secret-committed-to-git-is-compromised",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "A secret committed to git is compromised; rotate it.",
    "why": "Deleting the commit is not a remedy.",
    "continues": false
  },
  {
    "id": "m24-scan-dependencies-in-ci",
    "kind": "explain",
    "tier": "module",
    "module": "24",
    "part": "Module 24 — Security for a .NET API",
    "href": "course/module-24-security/",
    "claim": "Scan dependencies in CI.",
    "why": "A transitive package runs with your privileges.",
    "continues": false
  },
  {
    "id": "m25-a-timeout-tells-you-nothing-about-whether",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "A timeout tells you nothing about whether the work happened.",
    "why": "The reply can be lost after the commit — which is why every retryable operation must be idempotent.",
    "continues": false
  },
  {
    "id": "m25-you-cannot-atomically-write-to-two-systems",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "You cannot atomically write to two systems.",
    "why": "Save-then-publish loses messages; publish-then-save invents them. Make it one write: the outbox.",
    "continues": false
  },
  {
    "id": "m25-exactly-once-delivery-does-not-exist",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Exactly-once delivery does not exist.",
    "why": "Choose at-least-once and make the consumer idempotent; that is what \"exactly-once processing\" really means.",
    "continues": false
  },
  {
    "id": "m25-idempotency-is-designed-in-not-added",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Idempotency is designed in, not added.",
    "why": "Unique constraints, a processed-message table, conditional updates, and setting values rather than incrementing them.",
    "continues": false
  },
  {
    "id": "m25-set-x-5-is-idempotent-set-x-x-1-is-not",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "`SET x = 5` is idempotent; `SET x = x - 1` is not.",
    "why": "When you must decrement, do it with a version check.",
    "continues": false,
    "checkpoints": [
      "SET x = 5",
      "SET x = x - 1"
    ]
  },
  {
    "id": "m25-never-retry-a-non-idempotent-operation-and",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Never retry a non-idempotent operation, and never retry a 4xx.",
    "why": "The answer will not change, and you have multiplied one bad request.",
    "continues": false
  },
  {
    "id": "m25-exponential-backoff-with-jitter",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Exponential backoff with jitter.",
    "why": "Fixed intervals synchronise thousands of clients into a thundering herd that keeps the recovering service down.",
    "continues": false
  },
  {
    "id": "m25-cap-total-elapsed-time-not-just-the-retry",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Cap total elapsed time, not just the retry count.",
    "why": "Otherwise the caller times out and retries on top of you.",
    "continues": false
  },
  {
    "id": "m25-a-circuit-breaker-protects-the-caller-as",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "A circuit breaker protects the caller as much as the callee.",
    "why": "Without one, their outage consumes all your threads and becomes your outage.",
    "continues": false
  },
  {
    "id": "m25-every-remote-call-has-a-timeout-and-the",
    "kind": "complete",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Every remote call has a timeout, and the cancellation token is threaded all the way down.",
    "stem": "Every remote call has a timeout,",
    "why": "",
    "continues": false
  },
  {
    "id": "m25-eventual-consistency-is-a-design-decision",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Eventual consistency is a design decision, not a defect",
    "why": "— but \"how stale may this be?\" is a question you must answer explicitly, per read.",
    "continues": false
  },
  {
    "id": "m25-a-saga-compensates-it-does-not-roll-back",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "A saga compensates; it does not roll back.",
    "why": "A refund is a new business fact, visible to the customer, not an undo.",
    "continues": false
  },
  {
    "id": "m25-order-is-guaranteed-only-within-a-partition",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Order is guaranteed only within a partition.",
    "why": "Partition by aggregate id, or make handlers order-insensitive with a version check.",
    "continues": false
  },
  {
    "id": "m25-propagate-the-trace-context",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Propagate the trace context.",
    "why": "One request across five services must be one trace, or you are debugging by timestamp.",
    "continues": false
  },
  {
    "id": "m25-liveness-and-readiness-are-different",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Liveness and readiness are different questions.",
    "why": "Conflating them turns a dependency's outage into a restart loop.",
    "continues": false
  },
  {
    "id": "m25-microservices-buy-independent-deployment-for",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Microservices buy independent deployment for independent teams.",
    "why": "The benefit is organisational; every cost is technical. At two teams you have paid for all of it and bought nothing — the default is a modular monolith.",
    "continues": false
  },
  {
    "id": "m25-cut-services-by-business-capability-never-by",
    "kind": "explain",
    "tier": "module",
    "module": "25",
    "part": "Module 25 — Distributed systems and integration",
    "href": "course/module-25-distributed-systems/",
    "claim": "Cut services by business capability, never by layer or table.",
    "why": "The seam is where the vocabulary changes; services that must be released together are a distributed monolith.",
    "continues": false
  },
  {
    "id": "m26-single-responsibility-is-about-reasons-to",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Single responsibility is about REASONS TO CHANGE, not size.",
    "why": "Two stakeholders who can each demand a change to one class means two responsibilities.",
    "continues": false
  },
  {
    "id": "m26-dependency-inversion-means-the-high-level",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Dependency inversion means the high-level module owns the abstraction.",
    "why": "`IOrderRepository` lives in Application, not next to its implementation — that placement is what makes the dependency arrow point inward.",
    "continues": false,
    "checkpoints": [
      "IOrderRepository"
    ]
  },
  {
    "id": "m26-a-subtype-must-be-substitutable-without-the",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "A subtype must be substitutable without the caller knowing.",
    "why": "A derived member that throws `NotSupportedException` is a Liskov violation, and `sealed` is a good default because of it.",
    "continues": false,
    "checkpoints": [
      "NotSupportedException",
      "sealed"
    ]
  },
  {
    "id": "m26-never-expose-iqueryable-from-a-repository",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Never expose `IQueryable` from a repository.",
    "why": "The moment you do, the abstraction is decorative and EF Core has leaked into the layer that was supposed to be free of it.",
    "continues": false,
    "checkpoints": [
      "IQueryable"
    ]
  },
  {
    "id": "m26-a-pattern-is-an-answer-to-a-problem-you-can",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "A pattern is an answer to a problem you can state.",
    "why": "If you cannot say what would go wrong without it, you do not need it yet.",
    "continues": false
  },
  {
    "id": "m26-duplicate-twice-abstract-on-the-third",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Duplicate twice; abstract on the third.",
    "why": "A wrong abstraction costs far more than duplication, because everything else gets built on top of it.",
    "continues": false
  },
  {
    "id": "m26-prefer-a-static-factory-method-to-a-factory",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Prefer a static factory method to a factory class",
    "why": ", and let it return a `Result` so an invalid object cannot be constructed at all.",
    "continues": true,
    "checkpoints": [
      "Result"
    ]
  },
  {
    "id": "m26-injecting-iserviceprovider-hides-your",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Injecting `IServiceProvider` hides your dependencies.",
    "why": "Constructor parameters are a public declaration of what a class needs; resolving from the container is a secret.",
    "continues": false,
    "checkpoints": [
      "IServiceProvider"
    ]
  },
  {
    "id": "m26-map-explicitly-across-a-published-boundary",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Map explicitly across a published boundary.",
    "why": "A renamed property should be a compile error, not a null at run time.",
    "continues": false
  },
  {
    "id": "m26-primitive-obsession-is-a-type-system-problem",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "Primitive obsession is a type-system problem with a simple fix.",
    "why": "Two adjacent `string` parameters can be swapped silently; `Sku` and `CustomerName` cannot.",
    "continues": false,
    "checkpoints": [
      "string",
      "Sku",
      "CustomerName"
    ]
  },
  {
    "id": "m26-the-gof-singleton-is-a-global-variable",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "The GoF singleton is a global variable.",
    "why": "Use the container's singleton lifetime, which is substitutable in a test.",
    "continues": false
  },
  {
    "id": "m26-an-anemic-domain-model-is-the-default-not-a",
    "kind": "explain",
    "tier": "module",
    "module": "26",
    "part": "Module 26 — Design patterns and SOLID, in this codebase",
    "href": "course/module-26-patterns-and-solid/",
    "claim": "An anemic domain model is the default, not a design.",
    "why": "Behaviour belongs next to the data it protects.",
    "continues": false
  },
  {
    "id": "m27-the-c-version-is-chosen-by-the-target",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "The C# version is chosen by the target framework, not the SDK.",
    "why": "`net10.0` means C# 14; `global.json` pins the SDK for build reproducibility, and the TFM picks the language.",
    "continues": false,
    "checkpoints": [
      "net10.0",
      "global.json"
    ]
  },
  {
    "id": "m27-the-four-that-changed-the-language-are",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "The four that changed the language are generics, LINQ, async/await and nullable reference types.",
    "why": "Everything else is refinement.",
    "continues": false
  },
  {
    "id": "m27-generics-were-added-for-boxing-and-type",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "Generics were added for boxing and type safety, not syntax",
    "why": "— and unlike Java's they are a runtime feature, which is why `typeof(List<int>)` exists.",
    "continues": false,
    "checkpoints": [
      "typeof(List<int>)"
    ]
  },
  {
    "id": "m27-linq-needed-four-features-to-exist",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "LINQ needed four features to exist",
    "why": ": extension methods, lambdas, anonymous types and expression trees. The last one is the entire C#-to-SQL border.",
    "continues": true
  },
  {
    "id": "m27-foreach-was-fixed-in-c-5-for-was-not",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "`foreach` was fixed in C# 5; `for` was not.",
    "why": "A `for` loop still captures one shared variable — and it is still the loop you write when building a pipeline.",
    "continues": false,
    "checkpoints": [
      "foreach",
      "for"
    ]
  },
  {
    "id": "m27-exception-filters-when-run-before-the-stack",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "Exception filters (`when`) run before the stack unwinds.",
    "why": "That is why they preserve a better crash dump than catch-log-rethrow.",
    "continues": false,
    "checkpoints": [
      "when"
    ]
  },
  {
    "id": "m27-readonly-struct-and-ref-struct-are",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "`readonly struct` and `ref struct` are performance features with semantics",
    "why": ": one stops defensive copies, the other guarantees stack-only, which is what makes `Span<T>` safe.",
    "continues": true,
    "checkpoints": [
      "readonly struct",
      "ref struct",
      "Span<T>"
    ]
  },
  {
    "id": "m27-nullable-reference-types-are-compile-time",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "Nullable reference types are compile-time only",
    "why": "— the single most valuable feature for a business codebase, and it still does not check anything at run time.",
    "continues": false
  },
  {
    "id": "m27-static-abstract-interface-members-enable",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "`static abstract` interface members enable generic math, and cannot appear in an expression tree.",
    "why": "That collision is where EF Core meets modern C#.",
    "continues": false,
    "checkpoints": [
      "static abstract"
    ]
  },
  {
    "id": "m27-system-threading-lock-c-13-makes-do-not-lock",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "`System.Threading.Lock` (C# 13) makes \"do not lock on a shared object\" a type-level rule",
    "why": "rather than a convention nobody enforces.",
    "continues": false,
    "checkpoints": [
      "System.Threading.Lock"
    ]
  },
  {
    "id": "m27-the-field-keyword-c-14-removes-the-backing",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "The `field` keyword (C# 14) removes the backing field",
    "why": ", which is the most common boilerplate left in the language.",
    "continues": true,
    "checkpoints": [
      "field"
    ]
  },
  {
    "id": "m27-know-what-the-version-you-are-interviewing",
    "kind": "explain",
    "tier": "module",
    "module": "27",
    "part": "Module 27 — C# version by version",
    "href": "course/module-27-csharp-versions/",
    "claim": "Know what the version you are interviewing for supports.",
    "why": "Being able to say \"that needs C# 11, so on .NET 6 you would write it this way\" is worth more than knowing the newest feature.",
    "continues": false
  },
  {
    "id": "m28-you-are-hired-for-level-3-and-the-job-is-the",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "You are hired for Level 3, and the job is the two boundaries.",
    "why": "Below is a machine with no schema and no patience; above is an ERP that thinks in money. The middle is ordinary C#.",
    "continues": false
  },
  {
    "id": "m28-the-machine-layer-has-no-schema",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "The machine layer has no schema.",
    "why": "A register is a number at an address, and units, scaling, word order and what counts as `running` all live in a spreadsheet outside the protocol.",
    "continues": false,
    "checkpoints": [
      "running"
    ]
  },
  {
    "id": "m28-a-value-is-a-sample-not-the-truth",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "A value is a sample, not the truth.",
    "why": "It was taken at a time that is not now, and asking again gets you a different one rather than the same one confirmed.",
    "continues": false
  },
  {
    "id": "m28-never-poll-for-events",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Never poll for events.",
    "why": "Subscribe. The interesting things on a line are shorter than any interval you can afford, and the ones that matter most are the shortest.",
    "continues": false
  },
  {
    "id": "m28-two-timestamps-always",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Two timestamps, always",
    "why": "— when the machine says it was true, and when you received it. One column throws away the only latency measurement the protocol gives you for free.",
    "continues": false
  },
  {
    "id": "m28-store-quality-never-just-the-value",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Store quality, never just the value.",
    "why": "`Bad` and `Uncertain` are readings, not nulls, and averaging them with good ones invents data that nobody measured.",
    "continues": false,
    "checkpoints": [
      "Bad",
      "Uncertain"
    ]
  },
  {
    "id": "m28-the-machine-does-not-wait-for-your-consumer",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "The machine does not wait for your consumer.",
    "why": "Backpressure is a choice between losing the past and losing the present, and it must be made deliberately rather than defaulted into.",
    "continues": false
  },
  {
    "id": "m28-store-the-events-and-compute-the-number",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Store the events and compute the number.",
    "why": "A stored OEE cannot be explained, cannot be recomputed when the definition changes, and the definition will change.",
    "continues": false
  },
  {
    "id": "m28-whoever-defines-planned-downtime-sets-the",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Whoever defines planned downtime sets the OEE.",
    "why": "The same shift is 84% or 76% depending on one decision that involves no machine at all.",
    "continues": false
  },
  {
    "id": "m28-performance-above-100-is-a-data-quality",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Performance above 100% is a data-quality alarm, not a good day.",
    "why": "The configured ideal cycle time is wrong, or somebody ran the line over its rated speed.",
    "continues": false,
    "checkpoints": [
      "100"
    ]
  },
  {
    "id": "m28-traceability-is-a-legal-obligation-not-a",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Traceability is a legal obligation, not a feature.",
    "why": "In food, pharma and automotive the question is which lot, which machine, which shift — and the answer must survive years.",
    "continues": false
  },
  {
    "id": "m28-deadlock-on-a-floor-is-prevented-not",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Deadlock on a floor is prevented, not detected.",
    "why": "Acquire zones in a total order or grant a whole route atomically, because nothing times out when two vehicles are nose to nose.",
    "continues": false
  },
  {
    "id": "m28-zone-size-is-the-throughput-dial",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Zone size is the throughput dial",
    "why": ", and tuning it beats clever code — coarse zones serialise moves that never actually conflict.",
    "continues": true
  },
  {
    "id": "m28-the-line-is-on-an-isolated-network-on",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "The line is on an isolated network on purpose.",
    "why": "Data leaves OT through a gateway, outward only, and \"we'll just put it in the cloud\" is a proposal, not a plan.",
    "continues": false
  },
  {
    "id": "m28-assume-you-may-not-patch-the-hmi",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Assume you may not patch the HMI.",
    "why": "A Windows box from 2009 whose vendor warranty forbids you to touch it is normal; you compensate around it rather than fixing it.",
    "continues": false
  },
  {
    "id": "m28-commissioning-is-the-job-not-the-end-of-it",
    "kind": "explain",
    "tier": "module",
    "module": "28",
    "part": "Module 28 — Industrial software and the IT/OT boundary",
    "href": "course/module-28-industrial-and-ot/",
    "claim": "Commissioning is the job, not the end of it.",
    "why": "Half of this work happens on site, with the line stopped and people waiting, and the candidate who knows that is the one who lasts.",
    "continues": false
  }
];
