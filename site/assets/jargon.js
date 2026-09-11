/* ==========================================================================
   jargon.js — every professional word on this site, said in plain English.

   THE PROBLEM THIS SOLVES
   A chapter can be perfectly correct and still unreadable, because one word in
   the sentence is a word the reader has never been told the meaning of. They
   do not stop and look it up; they keep reading with a hole in the sentence,
   and by the third hole the paragraph means nothing. The glossary page does
   not fix it either — leaving the page to look something up is exactly what
   nobody does mid-paragraph.

   So the definition comes to the word. jargon-marks.js underlines the first
   appearance of each of these terms in a chapter and shows the one-liner in a
   small popover, without leaving the sentence.

   THE RULES FOR WRITING ONE
     * One sentence. If it needs two, the second goes in `note:` and is a
       warning or a contrast, never more definition.
     * No jargon inside the definition. "Deserialisation is the inverse of
       serialisation" helps nobody. If a word in the definition is itself in
       this list, rewrite it.
     * Say what it IS, not what it is FOR. "A DTO is good practice" is not a
       definition; "a plain class whose only job is to carry data between two
       places" is.
     * Accept being slightly imprecise. This is the ladder, not the roof: the
       precise version is in the chapter, which is what `where:` points at.

   THE FIELDS
     t      the term, in the form you would see it in prose
     plain  the one-liner. This is the whole point of the file.
     note   optional second line: a contrast, a warning, a "not to be confused"
     where  optional chapter number (matching chapters.js `n`) that explains it
     also   optional extra spellings that should match the same entry. A plain
            trailing "s" is handled automatically, so this is for irregulars:
            "-ise/-ize", "idempotency", "queries".

   WHAT IS DELIBERATELY NOT HERE
   Ordinary English words that happen to have a technical meaning too —
   "state", "index", "claim", "branch", "commit", "view", "layer", "spike".
   The marker cannot tell which sense is meant, and a confident wrong
   definition is worse than none. Where the technical sense matters it is
   listed as an unambiguous phrase instead: "clustered index", "code smell",
   "container image".

   The Italian-to-English vocabulary is a different problem and lives in
   glossary.js. That one is about a word you cannot translate; this one is
   about a word you cannot decode.

   Covered in: site/README.md
   ========================================================================== */

window.JARGON = [

  /* ═══════════════════════════════════════════ the words about code itself */

  { t: "abstraction", where: "05",
    plain: "A simpler view of something complicated, with the messy parts hidden behind it.",
    note: "A steering wheel is an abstraction over the steering rack. You turn it without knowing what it moves." },

  { t: "leaky abstraction", where: "05",
    plain: "A simplification that keeps forcing you to know the complicated thing underneath anyway." },

  { t: "API",
    plain: "The set of things one piece of software lets another one ask it to do.",
    note: "Any public surface counts: a web API over HTTP, but also the public methods of a class." },

  { t: "boilerplate",
    plain: "Code you have to write that says nothing interesting — the same shape every time." },

  { t: "closure", where: "04",
    plain: "A small function that remembers the variables that were around it when it was written.",
    note: "The trap is that it remembers the variable, not the value the variable had at the time." },

  { t: "cohesion", where: "05",
    plain: "How much the things inside one class actually belong together." },

  { t: "coupling", where: "05",
    plain: "How much one piece of code has to know about another to work.",
    note: "Loose coupling means you can change one without touching the other. That is the whole game." },

  { t: "cyclomatic complexity", where: "05b",
    plain: "A count of the decision points in a method — roughly, how many different paths it has." },

  { t: "declarative",
    plain: "Saying what you want, not the steps to get it." },

  { t: "imperative",
    plain: "Saying the steps, one after another, rather than the result you want." },

  { t: "deterministic", where: "24",
    plain: "Same input, same result, every single time." },

  { t: "encapsulation", where: "03",
    plain: "Keeping a class's data private and letting the outside touch it only through its methods." },

  { t: "idempotent", where: "13", also: ["idempotency", "idempotence"],
    plain: "Doing it twice has exactly the same effect as doing it once.",
    note: "Deleting order 42 is idempotent. Adding one to a counter is not." },

  { t: "immutable", where: "02", also: ["immutability"],
    plain: "It can never change after it is created; to get a different value you make a new one." },

  { t: "mutable", where: "02",
    plain: "It can be changed in place after it is created." },

  { t: "idiomatic",
    plain: "Written the way people who know this language would normally write it." },

  { t: "inheritance", where: "03",
    plain: "A class taking on everything another class has, then adding or changing a bit." },

  { t: "polymorphism", where: "03",
    plain: "The same call doing different things depending on which object is actually behind it." },

  { t: "composition", where: "03",
    plain: "Building a thing out of other objects it holds, instead of inheriting from them." },

  { t: "invariant", where: "05b",
    plain: "Something that must be true about an object at all times, no matter what you do to it.",
    note: "\"An order always has at least one line\" is an invariant. The job of the class is to make breaking it impossible." },

  { t: "heuristic", where: "05",
    plain: "A rule of thumb: usually right, occasionally wrong, never a law." },

  { t: "orthogonal",
    plain: "Two things that have nothing to do with each other and can be changed separately." },

  { t: "canonical",
    plain: "The one official version that everything else agrees with." },

  { t: "recursion", where: "00c", also: ["recursive"],
    plain: "A method that calls itself on a smaller piece of the problem until there is nothing left to split." },

  { t: "refactor", where: "05b", also: ["refactoring"],
    plain: "Changing how code is written without changing what it does." },

  { t: "regression", where: "24",
    plain: "Something that used to work and does not any more." },

  { t: "seam", where: "05",
    plain: "A place in the code where you can swap one piece for another without editing anything around it." },

  { t: "blast radius",
    plain: "How much else breaks when this one thing changes or fails." },

  { t: "side effect", where: "05b",
    plain: "Anything a method does besides returning its answer: writing a file, changing a field, sending an email." },

  { t: "pure function", where: "05b",
    plain: "A method whose answer depends only on its arguments and that changes nothing else." },

  { t: "code smell", where: "05b",
    plain: "Something in the code that is not a bug but is usually a sign of one coming." },

  { t: "anti-pattern", where: "05",
    plain: "A solution that looks sensible, gets used a lot, and reliably makes things worse." },

  { t: "technical debt", where: "05b",
    plain: "A shortcut taken now that will cost more to live with later — and costs a little every week until it is paid." },

  { t: "legacy code", where: "16b",
    plain: "Code that is in production, that people depend on, and that nobody wants to touch." },

  { t: "greenfield",
    plain: "A project with no existing code — you choose everything." },

  { t: "brownfield",
    plain: "A project on top of code that already exists and already has users." },

  { t: "premature optimisation", where: "05b", also: ["premature optimization"],
    plain: "Making code faster before you have measured which part is actually slow." },

  { t: "big O", where: "06", also: ["big-O"],
    plain: "A rough way of saying how much slower something gets as the data grows." },

  { t: "guard clause", where: "05b",
    plain: "An early return at the top of a method that throws out the bad cases before the real work starts." },

  { t: "fail fast", where: "05b",
    plain: "Stop at the first sign of something wrong, instead of carrying on and corrupting more." },

  { t: "defensive programming", where: "05b",
    plain: "Writing code that assumes its callers will get it wrong." },

  { t: "single source of truth", where: "05b",
    plain: "One place that owns a fact, so two copies of it can never disagree." },

  { t: "DRY", where: "05b",
    plain: "Don't Repeat Yourself: one piece of knowledge should be written down once.",
    note: "It is about knowledge, not about text. Two lines that look alike but change for different reasons should stay two lines." },

  { t: "YAGNI", where: "05b",
    plain: "You Aren't Gonna Need It: do not build for a future that has not asked for anything yet." },

  { t: "KISS", where: "05b",
    plain: "Keep It Simple: the boring version a tired colleague can read at midnight usually wins." },

  { t: "separation of concerns", where: "23",
    plain: "Each part of the system worries about one thing and trusts the others with theirs." },

  { t: "cross-cutting concern", where: "23",
    plain: "Something every part of the app needs — logging, validation, transactions — that belongs to none of them." },

  { t: "design pattern", where: "05",
    plain: "A solution somebody else already debugged, with a name attached so you can say it in one word." },

  { t: "SOLID", where: "05",
    plain: "Five pieces of advice about where to leave yourself room to change code later." },

  { t: "God object", where: "05b",
    plain: "One class that ended up doing everything, so every change has to touch it." },

  { t: "spaghetti code",
    plain: "Code where following what happens means jumping around so much you lose your place." },

  { t: "dependency", where: "05",
    plain: "Anything your code needs in order to work: another class, a library, a database.",
    note: "A dependency is something you cannot change on your own schedule. That is why the count of them matters." },

  { t: "dependency injection", where: "14", also: ["dependency inversion"],
    plain: "Handing a class the things it needs from outside, instead of it building them itself." },

  { t: "inversion of control", where: "14",
    plain: "Your code stops calling the framework and the framework starts calling your code." },

  { t: "DI container", where: "14",
    plain: "A registry that knows how to build your classes and hands them their dependencies for you." },

  { t: "singleton", where: "14",
    plain: "Exactly one instance, shared by the whole application, for its whole life.",
    note: "Shared means every request touches the same object. Anything changeable inside it is a concurrency bug waiting." },

  { t: "scoped", where: "14",
    plain: "One instance per web request: everything handling that request shares it, the next request gets a new one." },

  { t: "transient", where: "14",
    plain: "Short-lived: of an error, one that goes away if you retry; of a registered service, a brand-new instance every time." },

  { t: "decorator", where: "05",
    plain: "A wrapper with the same shape as the thing it wraps, which adds one behaviour and passes the rest on." },

  /* ═════════════════════════════════════════ the language and the runtime */

  { t: "value type", where: "00b",
    plain: "A type whose variable holds the value itself, so assigning it makes a copy." },

  { t: "reference type", where: "00b",
    plain: "A type whose variable holds the address of an object, so assigning it copies the address, not the object." },

  { t: "boxing", where: "02",
    plain: "Wrapping a value type in an object so it can be treated like one — which costs an allocation every time." },

  { t: "heap", where: "01",
    plain: "The memory where objects live, looked after for you by the garbage collector." },

  { t: "call stack", where: "07",
    plain: "The list of methods that are currently part-way through, most recent first." },

  { t: "stack trace", where: "07",
    plain: "The printed call stack from the moment something went wrong — the most useful lines in any error." },

  { t: "allocation", where: "01", also: ["allocate", "allocating"],
    plain: "Asking the runtime for a fresh piece of memory to put an object in." },

  { t: "garbage collection", where: "01", also: ["garbage collector"],
    plain: "The runtime noticing that nothing points at an object any more and taking its memory back." },

  { t: "memory leak", where: "08",
    plain: "Memory that is never given back because something is still holding on to it by mistake." },

  { t: "runtime", where: "01",
    plain: "The program that runs your compiled program: it hands out memory, collects the garbage and calls your code." },

  { t: "JIT", where: "01",
    plain: "Just-In-Time: the runtime turning your code into machine code the first time each method runs." },

  { t: "assembly", where: "01",
    plain: "One compiled .dll or .exe — the unit .NET actually loads." },

  { t: "namespace", where: "00a",
    plain: "A surname for types, so two libraries can both have a class called Order." },

  { t: "generics", where: "04", also: ["generic type"],
    plain: "Writing a class or method once with the type left as a blank, filled in by whoever uses it." },

  { t: "delegate", where: "04",
    plain: "A variable that holds a method, so behaviour can be passed around like data." },

  { t: "lambda", where: "04",
    plain: "A small unnamed method written inline, right where it is used." },

  { t: "extension method", where: "06",
    plain: "A method you write that looks as though it belongs to someone else's type." },

  { t: "reflection", where: "04",
    plain: "Code looking up or calling other code by its name, while the program is running." },

  { t: "type inference", where: "00b",
    plain: "The compiler working the type out for you, so you can write var instead of spelling it." },

  { t: "overload", where: "00c", also: ["overloading"],
    plain: "Several methods with the same name, told apart by what you pass them." },

  { t: "nullable", where: "00b",
    plain: "A type that is allowed to hold nothing at all as well as a real value." },

  { t: "null reference", where: "02",
    plain: "A variable that points at no object, which is why calling anything on it blows up." },

  { t: "exception", where: "07",
    plain: "The runtime abandoning what it was doing because it cannot carry on." },

  { t: "swallow", where: "07", also: ["swallowing", "swallowed"],
    plain: "Catching an error and then doing nothing about it, so the failure disappears silently." },

  { t: "serialisation", where: "13", also: ["serialization", "serialise", "serialize", "serialising", "serializing", "serialised", "serialized"],
    plain: "Turning an object into text or bytes you can send down a wire or save to disk." },

  { t: "deserialisation", where: "13", also: ["deserialization", "deserialise", "deserialize", "deserialising", "deserializing"],
    plain: "Rebuilding an object from the text or bytes it was turned into." },

  { t: "synchronous", where: "07",
    plain: "The caller waits, doing nothing else, until the work is finished." },

  { t: "asynchronous", where: "07",
    plain: "The caller hands the work off and is free to do something else until the answer arrives." },

  { t: "blocking", where: "07",
    plain: "Holding on to a thread while doing nothing but wait." },

  { t: "thread", where: "08",
    plain: "One line of execution: a program doing one thing at a time, in order." },

  { t: "thread-safe", where: "08", also: ["thread safety"],
    plain: "Safe for two threads to use at the same moment without corrupting anything." },

  { t: "race condition", where: "08",
    plain: "Two threads touching the same thing at once, where the result depends on which got there first." },

  { t: "deadlock", where: "08",
    plain: "Two things each waiting for something the other is holding, so neither ever moves again." },

  { t: "concurrency", where: "08",
    plain: "Several jobs in progress at once, taking turns." },

  { t: "parallelism", where: "08",
    plain: "Several jobs actually running at the same instant, on different cores." },

  { t: "atomic", where: "08",
    plain: "It either happens completely or not at all — nobody can ever see it half-done." },

  { t: "latency", where: "20",
    plain: "How long one request takes." },

  { t: "throughput", where: "20",
    plain: "How many requests you get through per second." },

  { t: "cache", where: "20", also: ["caching", "cached"],
    plain: "A copy of something kept somewhere faster, so you do not have to fetch it again." },

  { t: "cache invalidation", where: "20",
    plain: "Throwing the cached copy away when the real thing changes — the hard half of caching." },

  { t: "memoisation", where: "06", also: ["memoization"],
    plain: "Remembering the answer to a call so the same call does not have to work it out again." },

  { t: "deferred execution", where: "06",
    plain: "The query does not run when you write it — it runs when something asks for the results." },

  /* ═════════════════════════════════════════════════════════════════ data */

  { t: "schema", where: "09",
    plain: "The shape of the data: which tables exist, which columns they have, what is allowed in them." },

  { t: "primary key", where: "09",
    plain: "The column whose value identifies one row and never repeats." },

  { t: "foreign key", where: "09",
    plain: "A column holding another table's key, with the database refusing values that are not there." },

  { t: "referential integrity", where: "09",
    plain: "The database's promise that a row never points at a row that does not exist." },

  { t: "normalisation", where: "09", also: ["normalization", "normalised", "normalized"],
    plain: "Storing each fact in exactly one place, so it cannot be updated in one copy and not the other." },

  { t: "denormalisation", where: "09", also: ["denormalization", "denormalised", "denormalized"],
    plain: "Deliberately keeping a second copy of a fact, because reading it is worth the risk of the copies drifting." },

  { t: "clustered index", where: "11",
    plain: "The physical order the table's rows are actually stored in. There can only be one." },

  { t: "covering index", where: "11",
    plain: "An index that happens to hold every column a query asked for, so the table itself is never read." },

  { t: "execution plan", where: "11", also: ["query plan"],
    plain: "The step-by-step route the database chose to answer your query — and where the time went." },

  { t: "table scan", where: "11",
    plain: "Reading every row in the table, because there was no useful index." },

  { t: "cardinality", where: "11",
    plain: "How many different values a column has — few, like a yes/no flag, or many, like an email address." },

  { t: "transaction", where: "09",
    plain: "A group of changes that all happen or none do." },

  { t: "ACID", where: "09",
    plain: "The four promises a database transaction makes: all-or-nothing, always valid, not disturbed by others, and not lost once confirmed." },

  { t: "isolation level", where: "09",
    plain: "How much of other people's unfinished work your transaction is allowed to see." },

  { t: "dirty read", where: "09",
    plain: "Reading a change somebody else has made but not yet confirmed — and which may still vanish." },

  { t: "optimistic concurrency", where: "12",
    plain: "Let everyone edit, and reject the save if somebody else changed the row first." },

  { t: "pessimistic locking", where: "09",
    plain: "Lock the row while you are editing it, so nobody else can even start." },

  { t: "ORM", where: "12",
    plain: "A library that maps database rows to objects, so you write C# instead of SQL." },

  { t: "migration", where: "12",
    plain: "A recorded change to the database's shape, so every copy of the database can be brought to the same version." },

  { t: "N+1", where: "12",
    plain: "One query to get a list, then one more query for each item in it. Fifty rows, fifty-one round trips." },

  { t: "eager loading", where: "12",
    plain: "Fetching the related data in the same query, because you know you are going to need it." },

  { t: "change tracking", where: "12",
    plain: "The ORM quietly remembering which loaded objects you modified, so it knows what to write back." },

  { t: "connection pool", where: "09",
    plain: "A small set of database connections kept open and handed round, because opening one is expensive." },

  { t: "parameterised query", where: "09", also: ["parameterized query"],
    plain: "Sending the SQL and the values separately, so a value can never be read as SQL." },

  { t: "SQL injection", where: "15",
    plain: "A user typing something the database ends up running as code, because their input was glued into the query." },

  { t: "stored procedure", where: "10b",
    plain: "SQL saved inside the database itself and called by name." },

  { t: "CTE", where: "10",
    plain: "A named temporary result you define at the top of a query and then use as though it were a table." },

  { t: "window function", where: "10",
    plain: "A calculation across a set of related rows that still gives you one answer per row." },

  { t: "upsert", where: "10",
    plain: "Insert it if it is not there, update it if it is." },

  { t: "soft delete", where: "12",
    plain: "Marking a row as deleted instead of removing it, so it can come back and so history survives." },

  { t: "keyset pagination", where: "11",
    plain: "Fetching the next page by saying \"everything after this row\", rather than \"skip the first ten thousand\"." },

  { t: "sharding", where: "22b",
    plain: "Splitting one set of data across several databases, each holding a slice of it." },

  { t: "replication", where: "29",
    plain: "Keeping a second copy of the database up to date, usually to read from or to fail over to." },

  { t: "eventual consistency", where: "22",
    plain: "The copies disagree for a moment and then catch up. Correct, but you have to design for the moment." },

  /* ══════════════════════════════════════════════════════════════ the web */

  { t: "stateless", where: "13",
    plain: "The server remembers nothing between your requests, so every request has to carry what it needs." },

  { t: "REST", where: "13",
    plain: "A style of web API where the URL names a thing and the HTTP verb says what to do to it." },

  { t: "endpoint", where: "14",
    plain: "One URL plus one verb, and the code that answers it." },

  { t: "routing", where: "14",
    plain: "Deciding which piece of your code handles an incoming URL." },

  { t: "payload", where: "13",
    plain: "The actual content of a request or a response, as opposed to its headers." },

  { t: "status code", where: "13",
    plain: "The three-digit number saying how the request went: 200 fine, 404 no such thing, 500 we broke." },

  { t: "middleware", where: "14",
    plain: "A step every request passes through on the way in, and every response passes back through on the way out." },

  { t: "pipeline", where: "14",
    plain: "A chain of steps where each one hands its result to the next." },

  { t: "model binding", where: "14",
    plain: "The framework turning the raw request into the C# objects your method's parameters ask for." },

  { t: "DTO", where: "13",
    plain: "A plain class whose only job is to carry data between two places." },

  { t: "POCO", where: "12",
    plain: "An ordinary C# class, with no framework machinery bolted on." },

  { t: "CORS", where: "15",
    plain: "The browser rule that a page on one site cannot read a response from another unless that other site says it may." },

  { t: "CSRF", where: "15",
    plain: "Another site quietly making your browser send a request to one you are logged into." },

  { t: "XSS", where: "17",
    plain: "Somebody's input ending up as live script on your page, because it was not escaped." },

  { t: "authentication", where: "15",
    plain: "Working out who you are." },

  { t: "authorisation", where: "15", also: ["authorization"],
    plain: "Working out what you are allowed to do, once it is known who you are." },

  { t: "JWT", where: "15",
    plain: "A signed blob of facts about the user that the server can check without looking anything up." },

  { t: "bearer token", where: "15",
    plain: "A token that works for whoever is holding it, like a train ticket — which is why leaking one matters." },

  { t: "OAuth", where: "15",
    plain: "A protocol for letting one site act on your behalf on another, without it ever seeing your password." },

  { t: "hashing", where: "15", also: ["hashed"],
    plain: "Turning a value into a fixed-size fingerprint that cannot be turned back." },

  { t: "salt", where: "15",
    plain: "A random extra value mixed into each password before hashing, so two identical passwords do not look identical." },

  { t: "TLS", where: "15",
    plain: "The encryption behind the padlock: what turns HTTP into HTTPS." },

  { t: "least privilege", where: "15",
    plain: "Give every account exactly the access it needs and nothing more." },

  { t: "rate limiting", where: "15", also: ["throttling"],
    plain: "Refusing a caller's requests once they go over an allowed number per period." },

  { t: "back-pressure", where: "22",
    plain: "A slow consumer telling the producer to send less, instead of quietly drowning." },

  { t: "exponential backoff", where: "14b",
    plain: "Waiting longer before each retry — one second, then two, then four — instead of hammering." },

  { t: "jitter", where: "14b",
    plain: "A random wobble added to retry delays, so a thousand clients do not all retry at the same instant." },

  { t: "circuit breaker", where: "14b",
    plain: "After enough failures, stop calling the broken service at all for a while instead of waiting on every call." },

  { t: "webhook", where: "14b",
    plain: "They call your URL when something happens, instead of you asking repeatedly whether it has." },

  { t: "polling", where: "14b",
    plain: "Asking again and again whether anything has changed." },

  { t: "load balancer", where: "29",
    plain: "A front door that spreads incoming requests across several copies of your app." },

  { t: "reverse proxy", where: "29",
    plain: "A server that sits in front of yours, takes the request, and passes it on." },

  { t: "CDN", where: "17",
    plain: "Copies of your static files kept in data centres near your users." },

  /* ═══════════════════════════════════════════ shipping it and running it */

  { t: "CI", where: "30", also: ["continuous integration"],
    plain: "A machine that builds and tests every change automatically, as soon as it is pushed." },

  { t: "artifact", where: "30",
    plain: "The output of a build: the thing you actually deploy." },

  { t: "container image", where: "28",
    plain: "A frozen snapshot of an app plus everything it needs to run, which a container is started from." },

  { t: "orchestration", where: "28",
    plain: "Something that decides where containers run, restarts them when they die, and replaces them on deploy." },

  { t: "staging", where: "30",
    plain: "A copy of production, used for the last check before the real thing." },

  { t: "blue-green deployment", where: "30",
    plain: "Run the new version beside the old one and switch traffic over in one step, so going back is also one step." },

  { t: "canary release", where: "30",
    plain: "Send a small slice of traffic to the new version first, and watch it before sending the rest." },

  { t: "feature flag", where: "30",
    plain: "A switch that turns a feature on or off without deploying anything." },

  { t: "rollback", where: "30",
    plain: "Putting the previous version back, because the new one is worse." },

  { t: "hotfix", where: "25b",
    plain: "A small fix taken straight to production, because waiting for the next release is not an option." },

  { t: "health check", where: "25",
    plain: "A URL that answers \"am I actually working\", which everything upstream uses to decide whether to send you traffic." },

  { t: "observability", where: "25",
    plain: "Being able to work out what the system is doing from the outside, without attaching a debugger." },

  { t: "structured logging", where: "25",
    plain: "Logging named values rather than a sentence, so the log can be searched and counted instead of only read." },

  { t: "correlation id", where: "25",
    plain: "One id attached to every log line caused by the same request, so you can follow it across services." },

  { t: "distributed tracing", where: "25",
    plain: "A timeline of one request as it crosses several services, showing where the time actually went." },

  { t: "telemetry", where: "25",
    plain: "The measurements a running system gives off about itself: logs, numbers and timings." },

  { t: "percentile", where: "25", also: ["p95", "p99"],
    plain: "The value most requests came in under. p95 means 95 out of 100 were faster than this." },

  { t: "SLA", where: "25b",
    plain: "A written promise about availability or response time, usually with money attached to breaking it." },

  { t: "incident", where: "25b",
    plain: "Something is broken in production right now and somebody has to deal with it." },

  { t: "postmortem", where: "25b",
    plain: "The write-up after an incident: what happened, why, and what stops it happening again." },

  { t: "root cause", where: "25b",
    plain: "The thing that, had it not happened, would have meant none of the rest did." },

  { t: "triage", where: "25b",
    plain: "Deciding, quickly, what gets looked at first and what waits." },

  { t: "on-call", where: "25b",
    plain: "Being the person whose phone rings when production breaks at night." },

  /* ═══════════════════════════════════════════════════════════════ testing */

  { t: "unit test", where: "24",
    plain: "A test of one small piece of code on its own, with nothing real around it." },

  { t: "integration test", where: "24",
    plain: "A test of several pieces working together, usually with a real database behind them." },

  { t: "test double", where: "24",
    plain: "A stand-in you pass to the code under test instead of the real thing." },

  { t: "mock", where: "24", also: ["mocking", "mocked"],
    plain: "A stand-in that also records how it was called, so the test can check that it was." },

  { t: "stub", where: "24",
    plain: "A stand-in that simply returns a fixed answer." },

  { t: "fixture", where: "24",
    plain: "The prepared setup a test runs against, built the same way every time." },

  { t: "arrange-act-assert", where: "24",
    plain: "The three parts of a readable test: set it up, do the one thing, check the one result." },

  { t: "assertion", where: "24",
    plain: "The line of a test that says what must be true — and fails the test when it is not." },

  { t: "TDD", where: "24",
    plain: "Writing the failing test first, then the smallest code that passes it." },

  { t: "code coverage", where: "24",
    plain: "The share of your lines that ran while the tests ran.",
    note: "It tells you what was never executed. It does not tell you that what ran was actually checked." },

  { t: "mutation testing", where: "24",
    plain: "Deliberately breaking your own code to see whether any test notices." },

  { t: "flaky test", where: "24",
    plain: "A test that passes and fails on the same code, which trains everyone to ignore failures." },

  { t: "happy path", where: "24",
    plain: "The run where nothing goes wrong and every input is sensible." },

  { t: "edge case", where: "24",
    plain: "The input at the boundary — empty, zero, one, the maximum, the last day of the month." },

  { t: "smoke test", where: "30",
    plain: "A handful of checks after a deploy that answer \"is it alive at all\"." },

  /* ══════════════════════════════════════════ the team and the process */

  { t: "agile", where: "34",
    plain: "Working in short cycles and changing the plan as you learn, rather than planning it all up front." },

  { t: "scrum", where: "34",
    plain: "One specific way of being agile: fixed-length sprints, a few named meetings, a few named roles." },

  { t: "sprint", where: "34",
    plain: "A fixed stretch of time — usually two weeks — with an agreed set of work in it." },

  { t: "backlog", where: "34",
    plain: "The ordered list of everything not done yet." },

  { t: "user story", where: "35",
    plain: "One piece of work written from the point of view of whoever wants it, not of the code." },

  { t: "acceptance criteria", where: "35",
    plain: "The list that decides whether a piece of work is finished, agreed before it is started." },

  { t: "definition of done", where: "34",
    plain: "What the team agrees \"finished\" means — tested, reviewed, deployed — so nobody has a private version." },

  { t: "story point", where: "34",
    plain: "A rough size for a task, relative to other tasks, deliberately not in hours." },

  { t: "velocity", where: "34",
    plain: "How many points the team actually finished per sprint, used to forecast and for nothing else." },

  { t: "kanban", where: "34",
    plain: "A board of columns with a limit on how many things may sit in each, instead of fixed sprints." },

  { t: "MVP", where: "35",
    plain: "The smallest version worth putting in front of real users to find out whether you are right." },

  { t: "scope creep", where: "35",
    plain: "The job quietly growing after it was agreed, one small request at a time." },

  { t: "stakeholder", where: "35",
    plain: "Anyone whose work is affected by what you build, whether or not they are in the meeting." },

  { t: "pull request", where: "26",
    plain: "A request to merge your branch, with the changes laid out so colleagues can read them first." },

  { t: "code review", where: "26",
    plain: "A colleague reading your change before it is merged." },

  { t: "merge conflict", where: "26",
    plain: "Two people changed the same lines and the tool refuses to guess which one wins." },

  { t: "rebase", where: "26",
    plain: "Replaying your commits on top of somebody else's work, so the history reads as a straight line." },

  { t: "cherry-pick", where: "26",
    plain: "Taking one commit from one branch and applying it to another." },

  { t: "semantic versioning", where: "01", also: ["semver"],
    plain: "Version numbers with a meaning: the first goes up when you break something, the second when you add, the third when you fix." },

  { t: "breaking change", where: "13",
    plain: "A change that makes working code stop working for whoever was already using it." },

  { t: "deprecation", where: "13", also: ["deprecated"],
    plain: "Marking something as still working but on its way out, so people stop adopting it." },

  { t: "pair programming", where: "35",
    plain: "Two people, one piece of work, thinking out loud." },

  { t: "rubber ducking", where: "35",
    plain: "Explaining the problem out loud to anyone or anything, which is often where you spot it." },

  /* ════════════════════════════════════════════ the factory floor and ERP */

  { t: "ERP", where: "33",
    plain: "The system a company runs its money and its stock on: orders, invoices, warehouse, accounts." },

  { t: "MES", where: "32",
    plain: "The system that runs the production floor: what to make, on which machine, in what order, and what actually happened." },

  { t: "SCADA", where: "32",
    plain: "The screens an operator watches the plant on, with live values and alarms." },

  { t: "PLC", where: "32b",
    plain: "The small industrial computer that actually drives a machine, in a loop, thousands of times a second." },

  { t: "OPC UA", where: "32b",
    plain: "The standard way modern industrial machines let software read their values and send them commands." },

  { t: "fieldbus", where: "32b",
    plain: "The wiring standard that carries signals between a controller and the devices on a machine." },

  { t: "WMS", where: "32",
    plain: "The system that knows what is in the warehouse and where." },

  { t: "WCS", where: "32c",
    plain: "The layer that actually drives the warehouse's machinery — conveyors, cranes, sorters — moment to moment." },

  { t: "SKU", where: "32",
    plain: "One specific sellable item: this product, this size, this colour." },

  { t: "commissioning", where: "32c",
    plain: "The phase where the installed system is proved to work on the customer's real site, with their real machines." },

  { t: "downtime", where: "32",
    plain: "Time when the line is not producing — which is what the whole factory is measured on." },
];

/* The marker and the service worker both load this file with a plain <script>
   tag, so `window` is where it has to live. Nothing imports it and nothing
   bundles it: that is the whole architecture of this site. */
