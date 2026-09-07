/* ==========================================================================
   chapters.js — the single source of truth for the whole site.
   The sidebar, the home-page cards and every prev/next button are generated
   from this array. Add a chapter here and it appears everywhere.

   Each entry:
     n     chapter number shown in the sidebar ("" for the home page)
     id    file name inside /chapters (without .html)
     title sidebar + card title
     part  section heading it sits under
     blurb one line, shown on the home-page card
     tags  extra words the sidebar search should match
     req   which line of the job posting this chapter answers
     extra set INSTEAD of req when the chapter is not in that advert at all,
           but the regional market asks for it anyway. The home page renders
           these as a second table so the advert coverage stays honest.
   ========================================================================== */

const PARTS = [
  "Start here",
  "Part 1 — The platform and the language",
  "Part 2 — Data",
  "Part 3 — The web",
  "Part 4 — Fast at scale",
  "Part 5 — Building it properly",
  "Part 6 — Tools and shipping",
  "Part 7 — What the region actually runs",
  "Part 8 — The human requirements",
];

const CHAPTERS = [
  {
    n: "00", id: "00-the-job-posting", part: PARTS[0],
    title: "The job posting, decoded",
    blurb: "Every line of the advert translated into what it actually asks you to know.",
    tags: "requisiti annuncio requirements roadmap start",
    req: "the whole advert",
  },
  {
    n: "00b", id: "00b-how-a-project-is-built", part: PARTS[0],
    title: "How a C# project is built, in order",
    blurb: "The nine steps of one feature — entity, DbContext, migration, DTO, service, controller — and why that order.",
    tags: "order steps structure project layout scaffold model entity dto controller service dbcontext migration program.cs dependency injection folders feature slice beginner map how to start new project",
    extra: "No advert asks for it, and every interviewer does: “walk me through a request”, “what do you write first”. It is also the gap that makes every other chapter harder than it needs to be — you cannot place a file you have no map for.",
  },
  {
    n: "01", id: "01-dotnet-platform", part: PARTS[1],
    title: ".NET and .NET Core",
    blurb: "Runtime, SDK, CLR, JIT, assemblies — and why the Framework/Core split still matters.",
    tags: "runtime clr jit sdk nuget msbuild assembly il framework core",
    req: "Buona esperienza nello sviluppo con framework .NET / .NET Core",
  },
  {
    n: "02", id: "02-csharp-fundamentals", part: PARTS[1],
    title: "C# fundamentals",
    blurb: "Types, value vs reference, nullability, strings, and the syntax an interviewer probes.",
    tags: "csharp types struct class stack heap null string var record",
    req: "Ottima conoscenza del linguaggio C#",
  },
  {
    n: "03", id: "03-oop", part: PARTS[1],
    title: "Object-oriented programming",
    blurb: "Encapsulation, inheritance, polymorphism, abstraction — with the four SOLID answers.",
    tags: "oop inheritance polymorphism interface abstract virtual override solid",
    req: "familiarita con la programmazione ad oggetti (OOP)",
  },
  {
    n: "04", id: "04-generics-delegates-events", part: PARTS[1],
    title: "Generics, delegates and events",
    blurb: "The three language features every other chapter is quietly built on.",
    tags: "generic generics type parameter constraint covariance delegate func action predicate lambda closure event eventhandler multicast anonymous method callback",
    extra: "“What is a delegate?” and “why generics?” are asked in almost every junior technical round, and LINQ, DI, async and every event handler you will ever write are made of them.",
  },
  {
    n: "05", id: "05-solid-and-patterns", part: PARTS[1],
    title: "SOLID and design patterns",
    blurb: "The five principles in the version that actually helps, and the six patterns you will meet.",
    tags: "solid srp ocp lsp isp dip design pattern factory strategy repository decorator singleton adapter",
    extra: "Italian adverts name “principi SOLID” and “design pattern” directly, and interviewers ask you to name one you have used.",
  },
  {
    n: "06", id: "06-collections-and-linq", part: PARTS[1],
    title: "Collections and LINQ",
    blurb: "List, Dictionary, IEnumerable, deferred execution, and IQueryable vs IEnumerable.",
    tags: "linq list dictionary hashset ienumerable iqueryable deferred lambda",
    req: "Ottima conoscenza del linguaggio C#",
  },
  {
    n: "07", id: "07-async-and-errors", part: PARTS[1],
    title: "Async, await and exceptions",
    blurb: "What await really does, why .Result deadlocks, and how to handle failure properly.",
    tags: "async await task cancellation exception try catch deadlock threadpool",
    req: "Ottima conoscenza del linguaggio C#",
  },
  {
    n: "08", id: "08-disposal-and-thread-safety", part: PARTS[1],
    title: "Disposal, memory and shared state",
    blurb: "IDisposable and using, what the GC does, boxing, and making code safe under two threads.",
    tags: "idisposable dispose using idisposable pattern iasyncdisposable finalizer destructor garbage collection gc generation gen0 large object heap boxing unboxing memory leak race condition lock monitor deadlock interlocked concurrentdictionary semaphoreslim immutable thread safe",
    extra: "“Explain IDisposable”, “what does the garbage collector do” and “how would you make this thread-safe” are standard junior questions, and the answers are also where real production bugs come from.",
  },
  {
    n: "09", id: "09-databases-and-sql-server", part: PARTS[2],
    title: "Databases and SQL Server",
    blurb: "Tables, keys, relationships, data types, normalisation — the RDBMS vocabulary.",
    tags: "rdbms sql server database table primary key foreign normalisation schema",
    req: "Buona conoscenza dei principali RDBMS (in particolare SQL Server)",
  },
  {
    n: "10", id: "10-tsql-querying", part: PARTS[2],
    title: "T-SQL: querying",
    blurb: "SELECT, JOIN, GROUP BY, window functions, CTEs — and the order SQL really runs in.",
    tags: "tsql select join group by having cte window function rank subquery",
    req: "Buona conoscenza di T-SQL",
  },
  {
    n: "10b", id: "10b-views-functions-procedures", part: PARTS[2],
    title: "Views, functions and stored procedures",
    blurb: "The three ways a team saves a query inside the database — and which one is a trap.",
    tags: "view vista indexed view schemabinding materialised stored procedure sproc usp output parameter set nocount xact_abort try catch throw scope_identity parameter sniffing recompile scalar function udf inline table valued itvf tvf cross apply hasnokey toview hasdbfunction dapper exec",
    req: "Nozioni base di stored procedure, viste o funzioni SQL",
  },
  {
    n: "11", id: "11-tsql-performance", part: PARTS[2],
    title: "Indexes, plans and transactions",
    blurb: "The chapter that separates candidates: seeks vs scans, isolation levels, deadlocks.",
    tags: "index execution plan seek scan clustered transaction isolation deadlock lock acid",
    req: "Buona conoscenza di T-SQL e dei principali RDBMS",
  },
  {
    n: "12", id: "12-ef-core", part: PARTS[2],
    title: "Entity Framework Core",
    blurb: "DbContext, change tracking, migrations, loading strategies and the N+1 problem.",
    tags: "ef core entity framework dbcontext migration tracking include n+1 orm",
    req: "Conoscenza di Entity Framework Core (apprezzata)",
  },
  {
    n: "13", id: "13-rest-principles", part: PARTS[3],
    title: "REST principles",
    blurb: "Resources, verbs, status codes, statelessness, idempotency — and what REST is not.",
    tags: "rest http verb get post put patch delete status code idempotent stateless",
    req: "Comprensione dei principi REST",
  },
  {
    n: "14", id: "14-web-api", part: PARTS[3],
    title: "ASP.NET Core Web API",
    blurb: "Controllers, DI, middleware, model binding, validation, versioning, Swagger.",
    tags: "web api controller middleware dependency injection dto validation swagger openapi",
    req: "sviluppo di Web API",
  },
  {
    n: "14b", id: "14b-integrations", part: PARTS[3],
    title: "Talking to systems you do not own",
    blurb: "Typed clients, OAuth2, retries and circuit breakers, webhooks, and the CSV on the SFTP server.",
    tags: "integration integrazione external api third party httpclient ihttpclientfactory typed client socket exhaustion delegatinghandler oauth2 client credentials token cache polly resilience retry jitter circuit breaker timeout idempotency key 429 retry-after pagination anti-corruption webhook hmac signature replay raw body sftp csv fixed width encoding windows-1252 wiremock stub handler",
    extra: "“Integrazione tra sistemi web e piattaforme esterne” is a duty in one advert and repeated in its nice-to-haves, and another makes system integrations a core responsibility. Repeated on both halves of an advert means most of the sprint — and it is the material scattered thinnest across the rest of this site.",
  },
  {
    n: "15", id: "15-api-security", part: PARTS[3],
    title: "Securing an API",
    blurb: "Authentication vs authorization, what a JWT really is, and the OWASP mistakes that ship.",
    tags: "security jwt token oauth openid identity authentication authorization owasp cors https secret injection xss csrf",
    extra: "Every advert that says “Web API” means a secured one. JWT and “autenticazione/autorizzazione” are named explicitly in the ads, and this is the most common follow-up question after REST.",
  },
  {
    n: "16", id: "16-aspnet-mvc", part: PARTS[3],
    title: "ASP.NET Core MVC",
    blurb: "Model-View-Controller, Razor, tag helpers, view models — the server-rendered half.",
    tags: "mvc razor view controller tag helper viewmodel layout partial antiforgery",
    req: "Conoscenza di ASP.NET Core MVC (apprezzata)",
  },
  {
    n: "16b", id: "16b-aspnet-framework", part: PARTS[3],
    title: "The .NET Framework app you will inherit",
    blurb: "Classic ASP.NET: Global.asax, web.config, WebForms, IIS — and how to migrate it without a rewrite.",
    tags: "asp.net framework 4.8 legacy webforms viewstate postback global.asax httpmodule httphandler web.config binding redirect mvc 5 web api 2 apicontroller httpcontext.current synchronizationcontext deadlock configureawait iis application pool recycle session migration upgrade assistant yarp strangler systemwebadapters netstandard packages.config",
    extra: "Adverts that list “ASP.NET” and “ASP.NET Core” as two skills — or name .NET Framework 4.8 outright — are describing their repository. Every candidate can do the Core half; far fewer will open the 2011 solution, and one advert makes migration a duty.",
  },
  {
    n: "17", id: "17-frontend-basics", part: PARTS[3],
    title: "HTML, JavaScript and TypeScript",
    blurb: "Enough front-end to be useful: the DOM, fetch, async JS, and why types help.",
    tags: "html javascript typescript dom fetch json css frontend interface type",
    req: "Conoscenza base di tecnologie frontend (HTML, JavaScript, TypeScript)",
  },
  {
    n: "17b", id: "17b-javascript-language", part: PARTS[3],
    title: "JavaScript, the language",
    blurb: "The event loop, this, closures, promises and delegation — each with its C# equivalent alongside.",
    tags: "javascript js event loop microtask macrotask settimeout promise all allsettled unhandled rejection this arrow function bind closure var let const destructuring spread template literal map filter reduce sort foreach async await dom event bubbling delegation preventdefault esm commonjs import require jquery",
    extra: "One advert lists JavaScript as a required skill level with C# and SQL Server. Chapter 17 orients a backend developer in the browser; this treats the language as something you will be questioned on — and the two hard ideas in it, closures and cooperative scheduling, you already learned in C#.",
  },
  {
    n: "18", id: "18-spa-and-typescript", part: PARTS[3],
    title: "Single-page apps: Angular and React",
    blurb: "Components, the build step, calling your own API, and the contract between the two halves.",
    tags: "angular react spa single page application component npm vite typescript cors proxy openapi client generation state",
    extra: "Bologna ads pair “.NET” with “Angular” or “React” more often than with Razor. Even a backend role is usually expected to fix the screen its own API feeds.",
  },
  {
    n: "19", id: "19-blazor", part: PARTS[3],
    title: "Blazor",
    blurb: "C# in the browser: Server vs WebAssembly, render modes, and why the region cares.",
    tags: "blazor webassembly wasm interactive server render mode component signalr circuit winforms wpf migration desktop",
    extra: "Blazor is how the region's WinForms and WPF shops move to the web without hiring a JavaScript team — and Bologna adverts now name it directly.",
  },
  {
    n: "20", id: "20-redis-caching", part: PARTS[4],
    title: "Caching with Redis",
    blurb: "In-memory vs distributed, cache-aside, TTL, invalidation, stampedes.",
    tags: "redis cache distributed memory ttl invalidation stampede idistributedcache",
    req: "Esperienza con sistemi di caching (Redis)",
  },
  {
    n: "21", id: "21-search-elastic", part: PARTS[4],
    title: "Search with ElasticSearch / OpenSearch",
    blurb: "Inverted indexes, analyzers, relevance, and when a LIKE query is no longer enough.",
    tags: "elasticsearch opensearch lucene inverted index analyzer relevance bm25 fulltext",
    req: "Esperienza con sistemi di ricerca (ElasticSearch/OpenSearch)",
  },
  {
    n: "22", id: "22-messaging-and-queues", part: PARTS[4],
    title: "Message brokers and queues",
    blurb: "RabbitMQ, Kafka, Azure Service Bus — and the outbox that makes any of them safe.",
    tags: "message broker queue rabbitmq kafka azure service bus masstransit outbox idempotency at-least-once dead letter dlq event driven asynchronous decoupling",
    extra: "RabbitMQ, Kafka and Azure Service Bus travel with “microservizi” and “event-driven” through backend adverts in Milano and Bologna. The repository already has the outbox; this explains what it feeds.",
  },
  {
    n: "22b", id: "22b-microservices", part: PARTS[4],
    title: "Microservices, honestly",
    blurb: "What the architecture buys, what it costs, how to cut it — and why the answer here is usually the modular monolith.",
    tags: "microservices microservizi monolith modular monolith distributed monolith bounded context ddd seam independent deployment api gateway bff yarp service discovery saga compensation outbox eventual consistency availability chain strangler backgroundservice periodictimer scheduled job distributed lock lease idempotent hangfire quartz cronjob",
    extra: "“Microservizi” appears in the duties of one advert and again in its nice-to-haves, and the word occurs in this repository only where module 08 says CQRS is not it. The substance is taught in 22 and module 25; this claims the vocabulary and adds the judgement — including the scheduled-job problem that arrives with the second instance.",
  },
  {
    n: "23", id: "23-architecture-and-cqrs", part: PARTS[5],
    title: "Layers, clean architecture and CQRS",
    blurb: "Where a rule belongs, which way the arrows point, and what CQRS is really separating.",
    tags: "architecture clean architecture layer domain application infrastructure cqrs command query mediator dependency rule ddd",
    extra: "“Clean architecture”, “DDD” and “CQRS” appear in Italian backend adverts constantly, and the question “where would you put this rule?” is how a reviewer finds out whether you have ever worked on a real codebase.",
  },
  {
    n: "24", id: "24-testing", part: PARTS[5],
    title: "Testing",
    blurb: "The pyramid, xUnit and Moq, integration tests against a real database, and what not to test.",
    tags: "test testing unit xunit nunit moq mock stub fake integration test tdd assert arrange act testcontainers coverage",
    extra: "xUnit/NUnit and “test unitari” are on nearly every advert in the region, and a take-home exercise submitted without tests is the most common quiet rejection.",
  },
  {
    n: "25", id: "25-observability", part: PARTS[5],
    title: "Logging and observability",
    blurb: "Structured logs, correlation ids, health checks, metrics and traces — debugging what you cannot attach to.",
    tags: "logging log serilog structured logging correlation id health check metrics tracing opentelemetry application insights monitoring",
    extra: "Ads ask for “monitoraggio” and “troubleshooting in produzione”. Once your code runs on Azure App Service you cannot attach a debugger, so this is the skill that replaces one.",
  },
  {
    n: "25b", id: "25b-production-support", part: PARTS[5],
    title: "Incidents, and the documents you write",
    blurb: "Mitigate before you diagnose. Severity, symptom alerts, the blameless postmortem, and the ADR.",
    tags: "incident management troubleshooting bug fixing monitoraggio severity sev1 triage mitigate rollback feature flag alerting symptom cause runbook alert fatigue slo correlation id postmortem blameless five whys action item adr architecture decision record technical analysis release note handover minimal repro works on my machine",
    extra: "One advert makes incident management, monitoring and technical documentation three of its five responsibilities. Chapter 25 supplies the instruments; nothing yet covered the procedure — and “analisi e documentazione” is the vaguest bullet in any advert while meaning four very concrete artefacts.",
  },
  {
    n: "26", id: "26-git", part: PARTS[6],
    title: "Git",
    blurb: "Commits, branches, merge vs rebase, conflicts, pull requests — the daily commands.",
    tags: "git branch merge rebase conflict pull request commit stash cherry-pick svn tfvc",
    req: "Utilizzo di Git per il controllo di versione",
  },
  {
    n: "27", id: "27-visual-studio-vscode", part: PARTS[6],
    title: "Visual Studio and VS Code",
    blurb: "Debugging, breakpoints, watch windows, the CLI, and the shortcuts worth muscle memory.",
    tags: "visual studio vscode debug breakpoint watch immediate window dotnet cli shortcut",
    req: "Utilizzo fluido di Visual Studio e/o VS Code",
  },
  {
    n: "28", id: "28-containers-and-docker", part: PARTS[6],
    title: "Containers and Docker",
    blurb: "Images, layers, a Dockerfile for a .NET API, compose for the dependencies, and what Kubernetes adds.",
    tags: "docker container image layer dockerfile compose volume port kubernetes k8s orchestration registry",
    extra: "“Docker” and “microservizi” travel together through the Milano and Bologna ads, and every Redis / SQL Server / Elasticsearch “try it yourself” in this site is already a container.",
  },
  {
    n: "29", id: "29-azure", part: PARTS[6],
    title: "Azure",
    blurb: "App Service, SQL Database, Key Vault, Storage, Service Bus — the services you will touch.",
    tags: "azure cloud app service sql database key vault blob storage service bus paas",
    req: "Familiarita con servizi Cloud (Azure)",
  },
  {
    n: "29b", id: "29b-aws", part: PARTS[6],
    title: "AWS, for someone who knows Azure",
    blurb: "The translation table, IAM roles instead of keys, RDS for SQL Server, and how much to claim.",
    tags: "aws amazon ecs fargate app runner eks rds sql server s3 secrets manager parameter store cloudwatch x-ray lambda sqs sns iam role access key credential provider chain awssdk eu-south-1 milan cold start native aot snapstart billing alarm",
    extra: "AWS is the only optional skill in any of the four adverts audited — “skill facoltativa”, “gradita familiarità”. The goal is credibility rather than competence: enough to deploy something small, name the equivalents, and answer the follow-up without bluffing. Deliberately the shortest chapter here.",
  },
  {
    n: "30", id: "30-azure-devops-cicd", part: PARTS[6],
    title: "Azure DevOps and CI/CD",
    blurb: "Pipelines as YAML, build and release stages, artifacts, environments, approvals.",
    tags: "azure devops ci cd pipeline yaml build release artifact deploy boards repos",
    req: "strumenti di CI/CD (Azure DevOps)",
  },
  {
    n: "31", id: "31-desktop-wpf-winforms", part: PARTS[7],
    title: "The desktop that pays the bills",
    blurb: "WinForms and WPF: MVVM, the UI thread, and how to modernise without a rewrite.",
    tags: "wpf winforms desktop xaml mvvm inotifypropertychanged icommand binding dispatcher ui thread clickonce msix legacy migration strangler webview2 upgrade assistant net framework",
    extra: "Twenty-five-plus live WPF listings, and most industrial and gestionale work in the region has a desktop client. For many juniors the first job is maintaining one — and nobody teaches it, because courses teach the web.",
  },
  {
    n: "32", id: "32-industrial-and-mes", part: PARTS[7],
    title: "Industrial software and MES",
    blurb: "The shop floor: PLCs, OPC UA, shifts, OEE — and why this software is written differently.",
    tags: "mes industrial automation automazione industriale plc scada opc ua isa-95 oee shift batch traceability sensor time series manufacturing sassuolo packaging ot",
    extra: "Ads in Sassuolo, Fiorano Modenese, Bologna, Castel Bolognese and Ravenna ask for C#/.NET for automazione industriale and supervisioni. This is the region's actual economy, and almost no candidate prepares for it.",
  },
  {
    n: "33", id: "33-business-central", part: PARTS[7],
    title: "Dynamics 365 Business Central and AL",
    blurb: "The gestionale track: AL objects, events over overrides, extensions, and API pages.",
    tags: "business central dynamics 365 nav navision al language gestionale erp extension codeunit page table event subscriber odata api page appsource",
    extra: "Roles in Reggio Emilia and Bologna, and around 85 listings Italy-wide. It is a parallel .NET-adjacent career with far less competition than generic backend work.",
  },
  {
    n: "33b", id: "33b-ai-in-dotnet", part: PARTS[7],
    title: "AI in a .NET application",
    blurb: "An LLM is an unreliable external dependency. Calling one, grounding it, costing it, and testing it.",
    tags: "ai intelligenza artificiale llm microsoft.extensions.ai ichatclient azure openai ollama structured output prompt injection rag retrieval augmented generation embedding vector sql server vector_distance azure ai search token cost golden set hallucination gdpr eu ai act denial of wallet",
    extra: "One advert names AI twice — in the duties and again as an interest requirement — and it appears nowhere else in this repository. It is an interest requirement, not a competence one, so the goal is a credible twenty-minute conversation and the architectural judgement to be trusted near it.",
  },
  {
    n: "34", id: "34-agile-and-scrum", part: PARTS[8],
    title: "Agile, Scrum and the daily rhythm",
    blurb: "Sprints, stand-ups, refinement, story points — what the ceremonies are for and how to survive them.",
    tags: "agile scrum kanban sprint stand up daily retrospective refinement backlog story point estimate velocity definition of done ticket jira azure boards",
    extra: "“Metodologie Agile” and “Scrum” are on nearly every consulting advert (NTT Data, Accenture, Reply, Engineering). Nobody teaches this, and the first stand-up is where a junior looks lost.",
  },
  {
    n: "35", id: "35-analysis-and-teamwork", part: PARTS[8],
    title: "Analysis, autonomy and teamwork",
    blurb: "The soft requirements, made concrete: how to read a ticket, decide alone, and be reviewed.",
    tags: "analysis autonomy team working communication code review agile estimate soft skills",
    req: "Buone capacita di analisi e autonomia decisionale / orientamento al team working",
  },
  {
    n: "36", id: "36-cv-and-linkedin", part: PARTS[8],
    title: "Your CV, LinkedIn and this project",
    blurb: "The Italian CV conventions, the profile recruiters actually search, and how to present a repository.",
    tags: "cv curriculum vitae resume linkedin profilo portfolio github repository europass lettera di presentazione recruiter ats candidatura autocandidatura referenze",
    extra: "Everything else in this site matters only after somebody reads your CV. Italian conventions differ from the Anglo-Saxon ones, and most juniors present a portfolio in the way that helps them least.",
  },
  {
    n: "37", id: "37-the-screening-test", part: PARTS[8],
    title: "The screening test",
    blurb: "The timed automated round: complexity, the problem shapes that recur, and how to not run out of time.",
    tags: "screening test hackerrank codility assessment coding challenge algorithm complexity big o hash map dictionary two pointers sliding window recursion string manipulation edge case timed test tecnico prova pratica",
    extra: "The large consultancies filter with an automated test before a human sees you. This site told you the round exists; it never prepared you for it.",
  },
  {
    n: "38", id: "38-the-interview", part: PARTS[8],
    title: "The interview",
    blurb: "The questions this advert implies, the live-coding round, and what to ask them back.",
    tags: "interview colloquio questions live coding ral contratto emilia romagna modena bologna",
    req: "the whole advert, again — from the other side of the table",
  },
];

/* Make available to plain <script> pages (no modules — this must run on file://).

   `self`, not `window`. They are the same object in a page, and in a SERVICE WORKER
   there is no `window` at all — so writing `window.CHAPTERS` here would throw the
   moment sw.js does importScripts() on this file. It does exactly that, so the
   offline precache list is generated from this array rather than being a second
   copy of it that goes stale. */
self.PARTS = PARTS;
self.CHAPTERS = CHAPTERS;
