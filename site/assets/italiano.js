/* ==========================================================================
   italiano.js — the Italian layer.

   THE GAP THIS FILLS
   The viva already has a "rispondi in italiano" toggle, and the glossary has
   133 terms. Between them they cover vocabulary. What neither covers is the
   thing that actually goes wrong in a colloquio in Modena: you know the answer,
   you know the words, and you cannot assemble a sentence at speed — so you
   either drop into English mid-explanation or you simplify the answer down to
   what your Italian can carry, which makes you sound like you know less than
   you do.

   The fix is not more vocabulary. It is having said the sentence before.

   WHAT IS HERE
     PHRASES     — situational: opening, buying time, admitting a gap,
                   disagreeing politely, asking about the work, salary.
                   These are the ones that recur in every interview regardless
                   of the stack.
     CHAPTER_IT  — per chapter: the one or two sentences you would actually say
                   about that chapter's core idea, plus the terms that stay in
                   English (and there are many — nobody says "interrogazione"
                   for query).

   ON THE ENGLISH-INSIDE-ITALIAN THING
   Italian developers speak Italian with English technical nouns embedded, and
   they do not translate them: "la query", "il deploy", "fare il merge", "una
   race condition". Translating them is the marker of somebody who learned the
   words from a textbook. So the sentences below keep them in English, on
   purpose, and that is not laziness — it is how the language is actually used
   in this industry, in this region.

   COVERAGE IS PARTIAL, ON PURPOSE
   CHAPTER_IT does not cover all 48 chapters, and the panel simply does not
   appear where there is no entry. A half-written translation is worse than
   none: it teaches a sentence you would not want to say. Add entries as you
   find yourself stuck on a topic — see site/README.md.
   ========================================================================== */

/* ---------------------------------------------------------------- phrases */

self.IT_PHRASES = [
  {
    group: "Aprire",
    items: [
      {
        it: "Buongiorno, piacere di conoscerla. Grazie per il tempo.",
        en: "Good morning, pleased to meet you. Thank you for your time.",
        note: "Lei, not tu, until they switch. Wait for them to offer it — many will, quickly.",
      },
      {
        it: "Mi occupo di sviluppo backend in .NET, principalmente C#, ASP.NET Core ed Entity Framework.",
        en: "I work on backend development in .NET — mainly C#, ASP.NET Core and Entity Framework.",
        note: "The one sentence that gets asked for first. Have it memorised; it buys you the first thirty seconds.",
      },
      {
        it: "Ho seguito un percorso da autodidatta e ho costruito un progetto completo per applicare quello che studiavo.",
        en: "I taught myself, and built a complete project to apply what I was studying.",
        note: "Says the thing without apologising for it. Never open with 'purtroppo non ho esperienza'.",
      },
    ],
  },
  {
    group: "Guadagnare tempo",
    items: [
      {
        it: "Buona domanda. Mi lasci pensare un attimo.",
        en: "Good question. Let me think for a second.",
        note: "Three seconds of silence reads as competence. Filling it with 'allora… ehm…' does not.",
      },
      {
        it: "Vuole che parta dal caso semplice o dal caso concorrente?",
        en: "Would you like me to start from the simple case or the concurrent one?",
        note: "Buys time AND signals that you know there is a concurrent case.",
      },
      {
        it: "Se ho capito bene, mi sta chiedendo perché… è corretto?",
        en: "If I have understood correctly, you are asking me why… is that right?",
        note: "Reflecting the question back is standard practice, not a weakness — and it stops you answering the wrong one.",
      },
    ],
  },
  {
    group: "Non lo so",
    items: [
      {
        it: "Non l'ho mai usato in produzione. So che serve per…, ma non le racconto un'esperienza che non ho.",
        en: "I have never used it in production. I know it is for…, but I will not invent experience I do not have.",
        note: "The single most valuable sentence on this page. Interviewers test for bluffing and they are good at it.",
      },
      {
        it: "Non lo so, ma so dove andrei a cercarlo: partirei da…",
        en: "I do not know, but I know where I would look: I would start from…",
        note: "Converts a gap into a demonstration of method. Only say it if the second half is true.",
      },
      {
        it: "L'ho studiato ma non l'ho ancora messo in pratica.",
        en: "I have studied it but not yet put it into practice.",
        note: "Honest and common for a junior. Follow immediately with what you DID put into practice.",
      },
    ],
  },
  {
    group: "Spiegare e non essere d'accordo",
    items: [
      {
        it: "Dipende dal caso d'uso: se il carico è in lettura conviene…, se invece ci sono scritture concorrenti…",
        en: "It depends on the use case: for a read-heavy load…, but with concurrent writes…",
        note: "'Dipende' is a strong answer when followed by the two branches. On its own it is an evasion.",
      },
      {
        it: "Capisco il punto. La mia esperienza è stata diversa, però: nel mio caso…",
        en: "I take the point. My experience was different though: in my case…",
        note: "Disagreeing is often the thing being tested. Agreeing with everything reads as no opinion.",
      },
      {
        it: "Le faccio un esempio concreto, così è più chiaro.",
        en: "Let me give you a concrete example, it will be clearer.",
        note: "The single best move when an abstract answer is going badly. Move to a story you control.",
      },
    ],
  },
  {
    group: "Chiedere del lavoro",
    items: [
      {
        it: "Come è organizzato il team? Quante persone e con quali ruoli?",
        en: "How is the team organised? How many people and in what roles?",
      },
      {
        it: "Come arriva una modifica in produzione? Avete una pipeline di CI/CD?",
        en: "How does a change reach production? Do you have a CI/CD pipeline?",
        note: "Tells you more about daily life there than any question about technology.",
      },
      {
        it: "Chi fa le code review, e quanto spesso rilasciate?",
        en: "Who does code reviews, and how often do you release?",
      },
      {
        it: "Qual è la parte del sistema che vi dà più problemi in questo momento?",
        en: "Which part of the system is giving you the most trouble right now?",
        note: "Interviewers remember this one. It is the question of somebody thinking about the work.",
      },
      {
        it: "C'è un percorso di crescita definito, e chi mi seguirebbe all'inizio?",
        en: "Is there a defined growth path, and who would mentor me at the start?",
      },
    ],
  },
  {
    group: "Contratto e RAL",
    items: [
      {
        it: "Per un profilo junior in zona mi aspetterei una RAL nell'ordine di … Sono aperto a discuterne.",
        en: "For a junior profile in this area I would expect a gross annual salary in the region of… I am open to discussing it.",
        note: "RAL is retribuzione annua lorda — GROSS, annual, and it is what everyone quotes. Give a range with a reason. Module 17 has the regional figures.",
      },
      {
        it: "Che tipo di contratto prevedete, e a quale livello del CCNL?",
        en: "What type of contract do you offer, and at what CCNL level?",
        note: "Indeterminato / determinato / apprendistato, and the CCNL level sets the minimum. Asking is normal, not pushy.",
      },
      {
        it: "È previsto smart working, e in che misura?",
        en: "Is remote work available, and how much?",
        note: "'Smart working' is the Italian term. Saying 'remote work' will be understood, but this is the local phrase.",
      },
    ],
  },
  {
    group: "Chiudere",
    items: [
      {
        it: "Quali sono i prossimi passi del processo?",
        en: "What are the next steps in the process?",
      },
      {
        it: "La ringrazio. Il progetto mi interessa molto, resto a disposizione.",
        en: "Thank you. The project interests me a great deal, I remain available.",
      },
    ],
  },
];

/* ------------------------------------------------------- per-chapter lines */

self.IT_CHAPTERS = {
  "02-csharp-fundamentals": {
    say: [
      "In C# i tipi valore stanno normalmente sullo stack e i tipi riferimento sull'heap, ma la cosa importante è la semantica: assegnando una struct ne copio il contenuto, assegnando una classe copio solo il riferimento.",
      "Il boxing è quando un tipo valore viene incapsulato in un oggetto sull'heap: costa un'allocazione, ed è la fonte di allocazioni nascoste che si vede nei profiler.",
    ],
    keep: ["struct", "class", "boxing", "stack", "heap", "nullable reference types"],
  },
  "04-generics-delegates-events": {
    say: [
      "Un delegate è un tipo che rappresenta un metodo: mi permette di passare del comportamento come parametro.",
      "La differenza fondamentale è tra Func<T> ed Expression<Func<T>>: il primo è codice compilato, il secondo è un albero di espressioni che EF Core può tradurre in SQL.",
    ],
    keep: ["delegate", "event", "Func", "Action", "Expression", "closure"],
  },
  "05-solid-and-patterns": {
    say: [
      "SOLID sono cinque principi; quello che uso più spesso è il Dependency Inversion: dipendo da un'interfaccia, non dall'implementazione, così posso sostituirla nei test.",
      "Il repository pattern isola l'accesso ai dati, ma con EF Core va valutato: il DbContext è già un'unit of work.",
    ],
    keep: ["SOLID", "dependency injection", "repository", "unit of work", "pattern"],
  },
  "06-collections-and-linq": {
    say: [
      "IEnumerable esegue in memoria, IQueryable viene tradotto in SQL dal provider: se assegno un IQueryable a un IEnumerable, il filtro si sposta dal database al mio processo e me ne accorgo solo dai tempi.",
      "Una query LINQ è una ricetta, non un risultato: viene eseguita solo quando la materializzo, con ToList o con un foreach.",
    ],
    keep: ["LINQ", "IEnumerable", "IQueryable", "deferred execution", "ToList", "provider"],
  },
  "07-async-and-errors": {
    say: [
      "async non crea un thread, ne libera uno: mentre aspetto l'I/O il thread torna al thread pool invece di restare bloccato.",
      "Non uso mai .Result o .Wait su codice asincrono, perché in un contesto con SynchronizationContext porta a deadlock. Async fino in fondo.",
    ],
    keep: ["async", "await", "Task", "thread pool", "deadlock", "CancellationToken", "ConfigureAwait"],
  },
  "08-disposal-and-thread-safety": {
    say: [
      "IDisposable serve a rilasciare risorse non gestite in modo deterministico: uso using, così Dispose viene chiamato anche in caso di eccezione.",
      "Una race condition è quando due thread scrivono lo stesso stato senza sincronizzazione: il risultato dipende dall'ordine e non è riproducibile.",
    ],
    keep: ["IDisposable", "using", "lock", "race condition", "thread-safe", "Interlocked"],
  },
  "09-databases-and-sql-server": {
    say: [
      "Un indice non-clustered è una struttura separata che punta alle righe; il clustered definisce l'ordine fisico della tabella, e ce n'è uno solo.",
      "Le transazioni seguono le proprietà ACID; il livello di isolamento decide quali anomalie sono possibili.",
    ],
    keep: ["index", "clustered", "non-clustered", "ACID", "isolation level", "deadlock", "lock"],
  },
  "10-tsql-querying": {
    say: [
      "Una JOIN combina righe di due tabelle; la INNER tiene solo le corrispondenze, la LEFT tiene tutte le righe di sinistra.",
      "Una CTE rende leggibile una query complessa, e con RECURSIVE permette di percorrere una gerarchia.",
    ],
    keep: ["JOIN", "LEFT JOIN", "GROUP BY", "CTE", "window function", "OVER"],
  },
  "10b-views-functions-procedures": {
    say: [
      "Una view non memorizza dati: è una SELECT salvata che in esecuzione viene espansa dentro la query, quindi di per sé non velocizza niente. Serve a dare un contratto stabile sopra uno schema che cambia, e a dare permessi su alcune colonne senza darli sulla tabella.",
      "Le funzioni scalari le evito perché vengono eseguite riga per riga e l'optimizer non ci vede dentro: nel piano di esecuzione sembrano gratis mentre la query impiega minuti. Se mi serve una funzione uso una inline table-valued function, che di fatto è una view con parametri.",
    ],
    keep: ["view", "stored procedure", "inline table-valued function", "execution plan", "optimizer", "SET NOCOUNT ON", "parameter sniffing"],
  },
  "11-tsql-performance": {
    say: [
      "Se metto una funzione sulla colonna nel WHERE la query diventa non-SARGable e l'indice non viene usato: si vede subito nel piano di esecuzione come scan invece di seek.",
      "Guardo sempre il piano di esecuzione prima di aggiungere un indice: aggiungerne uno a caso peggiora le scritture senza migliorare le letture.",
    ],
    keep: ["execution plan", "SARGable", "index seek", "index scan", "statistics", "key lookup"],
  },
  "12-ef-core": {
    say: [
      "Il problema N+1 è una query per i genitori più una per ogni collezione figlia: si risolve con una proiezione o con Include, e il lazy loading lo causa senza che te ne accorga.",
      "Per le query di sola lettura uso AsNoTracking: senza change tracking risparmio memoria e tempo.",
    ],
    keep: ["EF Core", "N+1", "Include", "AsNoTracking", "change tracking", "migration", "DbContext"],
  },
  "13-rest-principles": {
    say: [
      "REST è basato sulle risorse e sui verbi HTTP: GET legge, POST crea, PUT sostituisce, PATCH modifica parzialmente, DELETE elimina.",
      "GET e PUT sono idempotenti, POST no: ripetere una POST crea due risorse, ed è per questo che serve una idempotency key sui pagamenti.",
    ],
    keep: ["REST", "idempotente", "status code", "endpoint", "payload", "idempotency key"],
  },
  "14-web-api": {
    say: [
      "Uso i Problem Details, RFC 9457, così tutti gli errori dell'API hanno la stessa forma e un codice che il client può interpretare.",
      "La versione la metto nell'URL, /api/v1/, perché è visibile nei log e nelle cache; l'alternativa è un header, ma diventa invisibile a chi debugga.",
    ],
    keep: ["Web API", "middleware", "model binding", "Problem Details", "versioning", "DTO"],
  },
  "14b-integrations": {
    say: [
      "Quando chiamo un sistema che non è mio gli esiti sono tre, non due: successo, errore e «non lo so». Il timeout è proprio il caso in cui l'operazione potrebbe essere andata a buon fine, quindi non rifaccio una POST senza una idempotency key, altrimenti creo l'ordine due volte.",
      "Uso sempre un typed client registrato con IHttpClientFactory: creare un HttpClient a ogni chiamata lascia i socket in TIME_WAIT e si esauriscono le porte, mentre tenerne uno statico per sempre non si accorge dei cambi di DNS.",
    ],
    keep: ["HttpClient", "IHttpClientFactory", "typed client", "timeout", "retry", "circuit breaker", "idempotency key", "webhook"],
  },
  "15-api-security": {
    say: [
      "Un JWT è firmato, non cifrato: chiunque può leggerne il contenuto, quindi non ci metto dati sensibili. La firma garantisce solo che non sia stato modificato.",
      "L'autenticazione dice chi sei, l'autorizzazione dice cosa puoi fare: sono due middleware distinti e l'ordine conta.",
    ],
    keep: ["JWT", "claim", "bearer token", "refresh token", "CORS", "OWASP", "SQL injection"],
  },
  "16b-aspnet-framework": {
    say: [
      "ASP.NET e ASP.NET Core condividono il nome e quasi nessun codice: il vecchio gira su System.Web, solo su IIS e Windows, con una pipeline a eventi e il web.config; il nuovo è cross-platform, con i middleware e la dependency injection integrata.",
      "La differenza che conta di più è l'async: su Framework bloccare con .Result va in deadlock, perché c'è un SynchronizationContext che ammette un thread alla volta sulla richiesta, mentre su Core non c'è e lo stesso codice spreca soltanto un thread. Per migrare non propongo una riscrittura: metto un reverse proxy davanti e sposto una rotta alla volta.",
    ],
    keep: ["ASP.NET Core", "System.Web", "web.config", "middleware", "SynchronizationContext", "deadlock", "ConfigureAwait", "WebForms", "ViewState", "application pool"],
  },
  "17b-javascript-language": {
    say: [
      "JavaScript ha un solo thread: quando lo stack si svuota vengono eseguiti tutti i microtask — le callback delle Promise e le continuation degli await — e solo dopo un macrotask come un setTimeout. Per questo un setTimeout a zero millisecondi non vuol dire «adesso».",
      "Le closure catturano la variabile, non il valore: è esattamente lo stesso problema del ciclo for in C#, e qui si risolve usando let al posto di var, perché var ha scope di funzione.",
    ],
    keep: ["event loop", "microtask", "macrotask", "Promise", "async", "await", "closure", "let", "var", "arrow function", "event delegation"],
  },
  "19-blazor": {
    say: [
      "Blazor Server tiene lo stato sul server e comunica con il browser tramite un circuito SignalR: il primo caricamento è veloce ma serve una connessione stabile.",
      "Blazor WebAssembly scarica il runtime .NET nel browser: parte più lentamente ma poi funziona anche offline.",
    ],
    keep: ["Blazor", "component", "render mode", "circuit", "WebAssembly", "SignalR"],
  },
  "20-redis-caching": {
    say: [
      "Uso il pattern cache-aside: leggo dalla cache, se non c'è vado al database e ci scrivo il risultato con una scadenza.",
      "La cache in memoria non è distribuita: con due istanze ognuna ha la sua copia e possono divergere senza dare errore.",
    ],
    keep: ["cache", "cache-aside", "TTL", "Redis", "distributed cache", "invalidation"],
  },
  "22-messaging-and-queues": {
    say: [
      "Con una coda il produttore e il consumatore sono disaccoppiati: se il consumatore è giù i messaggi restano in coda invece di perdersi.",
      "Il problema della doppia scrittura si risolve con il pattern outbox: scrivo il messaggio nella stessa transazione del dato, e un processo separato lo pubblica.",
    ],
    keep: ["queue", "message broker", "outbox", "at-least-once", "idempotente", "dead letter"],
  },
  "22b-microservices": {
    say: [
      "I microservizi risolvono un problema organizzativo, non tecnico: servono a far rilasciare team diversi in modo indipendente. Con un solo team paghi tutti i costi — la rete, niente transazioni, niente join fra i confini — e il beneficio non ce l'hai.",
      "Il default che propongo è il monolite modulare: confini interni veri e imposti dai test di architettura, un solo deploy e una sola transazione. Se poi i team crescono, i confini per dividere ci sono già.",
    ],
    keep: ["bounded context", "deploy", "outbox", "saga", "eventual consistency", "API gateway", "distributed monolith", "BackgroundService"],
  },
  "23-architecture-and-cqrs": {
    say: [
      "CQRS separa i comandi, che modificano lo stato, dalle query, che lo leggono: i due lati hanno esigenze diverse e possono avere modelli diversi.",
      "In Clean Architecture le dipendenze puntano verso l'interno: il dominio non conosce il database, e questo lo rende testabile senza infrastruttura.",
    ],
    keep: ["CQRS", "Clean Architecture", "dominio", "handler", "mediator", "aggregate"],
  },
  "24-testing": {
    say: [
      "Uso la piramide: molti unit test veloci, meno test di integrazione, pochissimi end-to-end. I test di integrazione girano su un database vero, perché l'in-memory provider non è un database.",
      "Un test deve fallire per un motivo solo: se ne verifica tre, quando diventa rosso non so cosa è rotto.",
    ],
    keep: ["unit test", "integration test", "mock", "xUnit", "coverage", "test pyramid"],
  },
  "25-observability": {
    say: [
      "Il logging strutturato salva campi, non stringhe: così posso cercare per OrderId invece di fare grep sul testo.",
      "Un trace distribuito segue una richiesta attraverso più servizi grazie al correlation id: senza, con più servizi si debugga a intuito.",
    ],
    keep: ["logging", "structured logging", "trace", "span", "correlation id", "metrics", "OpenTelemetry"],
  },
  "25b-production-support": {
    say: [
      "Durante un incident la prima cosa non è capire, è mitigare: se è uscita una release nell'ultima ora faccio rollback, altrimenti spengo la feature con un flag. Capire con calma viene dopo, quando gli utenti stanno di nuovo lavorando.",
      "Gli alert li metto sui sintomi, non sulle cause: la CPU alta durante un batch notturno è normale, mentre un tasso di 5xx sopra l'uno per cento vuol dire che qualcuno non riesce a lavorare. E il post-mortem è blameless, perché se si cerca il colpevole la gente smette di segnalare.",
    ],
    keep: ["incident", "rollback", "feature flag", "alert", "post-mortem", "blameless", "correlation id", "runbook", "ADR"],
  },
  "26-git": {
    say: [
      "Merge mantiene la storia com'è avvenuta, rebase la riscrive per renderla lineare: non faccio mai rebase su un branch già condiviso.",
      "Un conflitto si risolve leggendo entrambe le versioni, non scegliendo la propria per fretta.",
    ],
    keep: ["commit", "branch", "merge", "rebase", "pull request", "conflict", "cherry-pick"],
  },
  "28-containers-and-docker": {
    say: [
      "Un'immagine è il modello, il container è l'istanza in esecuzione. Uso un build multi-stage così l'immagine finale non contiene l'SDK.",
      "Il container gira come utente non-root, perché root nel container è root sul kernel dell'host.",
    ],
    keep: ["container", "image", "Dockerfile", "multi-stage", "volume", "docker compose"],
  },
  "29b-aws": {
    say: [
      "Su AWS l'equivalente della managed identity è un IAM role attaccato al container o alla macchina: l'SDK trova da solo le credenziali temporanee, quindi nell'applicazione non c'è nessuna chiave. Le access key a vita lunga sono la causa classica dei leak.",
      "Su RDS non sei sysadmin, quindi il BACKUP nativo non funziona: si passa da stored procedure che scrivono il file su S3. Su Azure me la cavo meglio, ma i concetti si mappano quasi uno a uno.",
    ],
    keep: ["IAM role", "access key", "SDK", "RDS", "backup", "S3", "ECS", "Fargate", "Lambda", "cold start"],
  },
  "30-azure-devops-cicd": {
    say: [
      "La pipeline compila, esegue i test e pubblica l'artefatto; il deploy è uno step separato, così posso rilasciare senza ricompilare.",
      "Le migrazioni del database le faccio come step della pipeline, non all'avvio dell'applicazione, altrimenti più istanze partono insieme e si sovrappongono.",
    ],
    keep: ["pipeline", "build", "release", "artifact", "YAML", "deploy", "rollback"],
  },
  "33b-ai-in-dotnet": {
    say: [
      "Per me una chiamata a un modello è una dipendenza esterna inaffidabile: timeout, retry con jitter, circuit breaker e un comportamento definito per quando non risponde. Vale esattamente quello che vale per una qualsiasi integrazione.",
      "Il modello non è deterministico, quindi non decide mai un'invariante di dominio: sta dietro un'interfaccia in infrastructure, propone e non dispone. E se mando la mail di un cliente a un servizio esterno è un trasferimento di dati personali, quindi servono una base giuridica e una region europea.",
    ],
    keep: ["LLM", "prompt", "token", "embedding", "RAG", "retry", "circuit breaker", "hallucination", "prompt injection", "GDPR"],
  },
  "34-agile-and-scrum": {
    say: [
      "Nello Scrum lavoriamo per sprint; nel daily dico cosa ho chiuso, cosa faccio oggi e se sono bloccato — non un resoconto minuto per minuto.",
      "Se una stima si rivela sbagliata lo dico subito: il problema non è sbagliare la stima, è dirlo l'ultimo giorno.",
    ],
    keep: ["sprint", "backlog", "daily", "retrospettiva", "story point", "stand-up"],
  },
};
