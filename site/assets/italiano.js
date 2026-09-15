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
   CHAPTER_IT does not cover all 79 chapters, and the panel simply does not
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
  "00e-console-output-and-input": {
    say: [
      "Quello che l'utente digita è sempre una stringa: convertirla è compito mio, e lo è anche decidere cosa succede quando non è convertibile.",
      "Per un valore digitato da una persona uso sempre TryParse, non Parse: un errore di battitura è la normalità, e le eccezioni servono per l'eccezionale. La cosa importante è non ignorare mai il valore di ritorno, perché in caso di fallimento il parametro out resta a zero, che a valle sembra una risposta legittima.",
      "ReadLine restituisce null a fine input, non stringa vuota: la stringa vuota vuol dire che l'utente ha premuto Invio, null vuol dire che input non ne arriverà più. Se controllo solo la stringa vuota, con l'input rediretto da file il ciclo del prompt gira all'infinito.",
      "Gli errori li scrivo su Console.Error e i risultati su Console.Out, così chi redirige l'output su file continua a vedere i messaggi di errore.",
      "Il valore restituito dal programma è l'exit code: zero vuol dire successo, ed è l'unica cosa che uno script o un server di build guarda. Un programma che stampa ERRORE e restituisce zero, per la pipeline è andato bene.",
    ],
    keep: ["console", "prompt", "exit code", "stdout", "stderr", "redirect", "TryParse", "Parse", "out", "args", "command line", "culture", "invariant", "trim"],
  },
  "02-csharp-fundamentals": {
    say: [
      "In C# i tipi valore stanno normalmente sullo stack e i tipi riferimento sull'heap, ma la cosa importante è la semantica: assegnando una struct ne copio il contenuto, assegnando una classe copio solo il riferimento.",
      "Il boxing è quando un tipo valore viene incapsulato in un oggetto sull'heap: costa un'allocazione, ed è la fonte di allocazioni nascoste che si vede nei profiler.",
    ],
    keep: ["struct", "class", "boxing", "stack", "heap", "nullable reference types"],
  },
  "02b-the-properties-of-data": {
    say: [
      "Di ogni valore ci sono otto cose vere: il tipo, il range, dove sta, che valore ha prima che qualcuno lo assegni, se è modificabile, se può essere null, cosa vuol dire \"uguale\", e quando smette di esistere. Il tipo lo scelgo io, le altre sette me le trovo, ed è lì che arrivano le sorprese.",
      "Assegnando un tipo valore copio il valore, assegnando un tipo riferimento copio l'indirizzo: l'oggetto resta uno solo, e modificarlo da un nome si vede dall'altro. Questo si chiama aliasing.",
      "Il default è pericoloso proprio perché è plausibile: uno zero sembra una quantità vera e un false sembra una decisione che qualcuno ha preso. Se \"non lo so\" è uno stato reale del dominio, il tipo deve essere nullable.",
      "Su una classe l'operatore == confronta i riferimenti, quindi due oggetti con gli stessi valori non sono uguali. Se mi serve l'uguaglianza per contenuto uso un record, che genera sia Equals sia GetHashCode — e se ne riscrivo uno devo riscrivere anche l'altro, altrimenti il Dictionary cerca nel bucket sbagliato.",
      "Il garbage collector non si chiede se un oggetto ti serve ancora, solo se è raggiungibile. Un handler a cui non hai fatto il -= è raggiungibile, ed è per questo che un memory leak in .NET non è memoria persa: è memoria che qualcosa riesce ancora a raggiungere.",
    ],
    keep: ["value type", "reference type", "aliasing", "boxing", "overflow", "checked", "default", "immutable", "nullable", "null", "equality", "Equals", "GetHashCode", "record", "garbage collector", "dispose", "memory leak", "StringBuilder"],
  },
  "02c-wildcards-and-wordplay": {
    say: [
      "L'underscore in C# dipende da dove sta: come discard vuol dire \"qui ci va qualcosa che non mi interessa\", in uno switch è il pattern che prende tutto il resto e va per ultimo, mentre un nome che comincia con underscore è solo la convenzione per i campi privati e per il compilatore non significa niente.",
      "Il punto interrogativo fa quattro lavori diversi: dopo un tipo lo rende nullable, come ?. salta la chiamata se l'oggetto è null, come ?? dà un valore di ripiego, e nel ternario non c'entra niente con null.",
      "Il punto esclamativo davanti a un accesso, il null-forgiving, a runtime non fa assolutamente niente: dice solo al compilatore di smettere di avvisarti. Se il valore era null prendi la stessa NullReferenceException di prima, senza più il warning che te l'aveva predetta.",
      "Attenzione ai wildcard di LIKE: % e _ nel testo scritto dall'utente allargano la ricerca, e questo non lo risolve la parametrizzazione, che serve contro la SQL injection. Sono due difese diverse e servono entrambe.",
      "const viene risolto a compile time e copiato dentro chi lo usa, quindi se cambio una const in una libreria chi era già compilato continua a usare il valore vecchio. readonly invece è assegnato a runtime nel costruttore. Per questo const lo tengo solo per le cose vere per definizione.",
    ],
    keep: ["discard", "wildcard", "pattern matching", "switch expression", "nullable", "null-conditional", "null-coalescing", "ternary", "null-forgiving", "required", "LIKE", "escape", "SQL injection", "const", "readonly", "ref", "out", "override", "rethrow", "stack trace"],
  },
  "02d-predict-the-output": {
    say: [
      "La divisione fra due int è una divisione intera: 5 / 2 fa 2, e la parte decimale non viene arrotondata, viene buttata via. E un cast sul risultato non recupera niente, perché la perdita è già avvenuta dentro l'espressione.",
      "Le stringhe sono immutabili, quindi una riga come s.ToUpper(); da sola non fa niente: il metodo costruisce una stringa nuova e quella riga la butta. Il compilatore non lo considera un errore.",
      "Se rimuovo un elemento dentro un foreach sulla stessa lista prendo una InvalidOperationException, e la prendo all'iterazione successiva, non sulla Remove: per questo togliere l'ultimo elemento a volte sembra funzionare.",
      "Una query LINQ non è un risultato, è una ricetta: se chiamo Count() due volte la esegue due volte. Con EF Core dietro sono due round trip al database. Faccio ToList() una volta e poi riuso.",
      "Le lambda catturano la variabile, non il valore, e un ciclo for ha una sola variabile per tutte le iterazioni: per questo stampano tutte l'ultimo valore. Con foreach non succede, perché la variabile di iterazione è nuova a ogni giro.",
    ],
    keep: ["integer division", "overflow", "immutable", "interning", "StringBuilder", "aliasing", "shallow copy", "deferred execution", "LINQ", "struct", "pass by value", "ref", "record", "with", "closure", "short-circuit", "finally"],
  },
  "02e-functions-and-the-rest-of-the-syntax": {
    say: [
      "Una firma di metodo risponde sempre alle stesse cinque domande, in quest'ordine: chi può chiamarlo, se appartiene al tipo o all'oggetto, cosa restituisce, come si chiama e cosa deve entrare.",
      "async non fa parte della firma che vede chi chiama: quello che vede è il Task. È solo il permesso di usare await nel corpo, e posso toglierlo restituendo direttamente il task senza rompere niente.",
      "Un extension method è un metodo static in una classe static con il primo parametro marcato this: il compilatore riscrive la chiamata nella normale chiamata statica. Non aggiunge niente al tipo originale, ed è esattamente così che funziona LINQ — per questo senza il using giusto quei metodi \"non esistono\".",
      "Un metodo con yield return diventa una state machine: chiamarlo non esegue niente del corpo, che parte solo quando qualcuno chiede il primo elemento. Per questo un'eccezione lanciata in cima salta fuori al foreach e non alla chiamata, e la validazione dei parametri va messa in un metodo wrapper non-iterator.",
      "Gli overload si scelgono a compile time dal tipo dichiarato, gli override a runtime dal tipo reale. Questa singola frase spiega anche virtual contro new e l'uguaglianza fra due int boxati.",
    ],
    keep: ["signature", "static", "async", "Task", "await", "overload", "out", "ref", "params", "named argument", "tuple", "deconstruction", "local function", "lambda", "Func", "Action", "extension method", "iterator", "yield return", "deferred execution", "init", "required", "indexer", "attribute", "nameof"],
  },
  "02f-numbers-and-math": {
    say: [
      "I soldi vanno in decimal, mai in double: double è in virgola mobile binaria, quindi un decimo non è rappresentabile esattamente e l'errore si accumula su una somma. decimal è in base dieci, quindi 0.1 è esatto e lo è anche il totale di una colonna.",
      "Math.Round di default fa il banker's rounding: le metà esatte vanno al pari più vicino, quindi 2.5 diventa 2 e 3.5 diventa 4. Per una fattura passo esplicitamente MidpointRounding.AwayFromZero, e lo passo dappertutto, perché il problema peggiore è lo stesso importo arrotondato in due modi diversi in due punti del sistema.",
      "L'IVA dentro un lordo si tira fuori con una divisione, non con una moltiplicazione: il netto è lordo diviso 1,22 e l'imposta è la differenza. Moltiplicare il lordo per 0,22 sovrastima l'imposta del 22% dell'imposta stessa.",
      "Se divido un totale in tre e arrotondo ogni quota, perdo un centesimo e nessuna modalità di arrotondamento lo risolve. Calcolo n-1 quote e faccio l'ultima uguale al totale meno la somma delle altre: a chi va il resto è una decisione di business, non tecnica.",
      "L'overflow di un int è silenzioso: int.MaxValue più uno diventa un grande numero negativo senza nessun errore. Nei progetti che gestiscono numeri seri attivo CheckForOverflowUnderflow nel csproj.",
    ],
    keep: ["decimal", "double", "float", "overflow", "checked", "banker's rounding", "MidpointRounding", "arrotondamento", "IVA", "netto", "lordo", "invariant culture", "NaN", "Random", "seed"],
  },
  "02g-strings": {
    say: [
      "Le stringhe sono immutabili: ogni metodo restituisce una nuova stringa e nessuno modifica l'originale, quindi una riga come s.Trim(); da sola non fa assolutamente niente e il compilatore non avvisa.",
      "Quando confronto due stringhe passo sempre una StringComparison: Ordinal per identificatori, SKU, path e chiavi, OrdinalIgnoreCase quando il maiuscolo non conta, e la cultura solo per ordinare del testo che legge una persona.",
      "Non scrivo mai a.ToLower() == b: alloca una stringa inutile e applica le regole di cultura a quello che in realtà è un identificatore — in turco la minuscola di I non è i, e il confronto smette di funzionare su quella macchina senza nessun errore.",
      "IndexOf restituisce -1 quando non trova niente, non un'eccezione: e -1 più 1 fa 0, quindi passarlo a Substring trasforma \"non trovato\" in \"tutta la stringa\".",
      "Uso StringBuilder quando la concatenazione è dentro un ciclo, non quando la stringa è lunga: ogni += ricopia tutto quello accumulato fino a lì, quindi un export da ottantamila righe diventa miliardi di caratteri copiati — e di solito viene scambiato per un problema di database.",
    ],
    keep: ["immutabile", "interning", "StringComparison", "Ordinal", "OrdinalIgnoreCase", "StringBuilder", "Split", "Join", "Substring", "range", "IndexOf", "Trim", "invariant culture", "CSV"],
  },
  "02h-booleans-and-conditions": {
    say: [
      "In C# non esiste la truthiness: if di un intero o di una stringa non compila, e questo toglie di mezzo un'intera famiglia di bug.",
      "&& fa short-circuit, & no: e lo short-circuit è una garanzia del linguaggio, non un'ottimizzazione, quindi mettere il controllo del null prima di quello che protegge è codice corretto. Con una sola e commerciale la guardia non protegge più niente e parte una NullReferenceException.",
      "&& lega più stretto di ||, quindi a || b && c si legge a || (b && c). Nel dubbio metto le parentesi: a runtime non costano niente.",
      "Un bool? ha tre stati, non due: testo sempre lo stato che voglio con == true, perché != false è vero anche per null — ed è così che un ordine che nessuno ha ancora approvato finisce nel ramo degli approvati.",
      "Una condizione con più di due clausole le do un nome, meglio ancora come proprietà del tipo a cui la domanda si riferisce: così un lettore capisce a cosa servivano quelle clausole e non possono divergere fra i vari punti di chiamata.",
    ],
    keep: ["bool", "truthiness", "short-circuit", "precedenza", "nullable", "bool?", "logica a tre valori", "guard clause", "pattern matching", "switch expression", "enum"],
  },
  "02i-arrays-and-shapes": {
    say: [
      "La lunghezza di un array è fissata alla creazione: Array.Resize non ridimensiona niente, alloca un nuovo array e ricopia tutto — se lo sto chiamando dentro un ciclo, volevo una List.",
      "Su un array rettangolare, .Length è il numero totale di celle, non di righe: righe e colonne sono GetLength(0) e GetLength(1). È l'errore più comune con gli array a due dimensioni.",
      "Un array jagged è un array di riferimenti ad altri array, quindi le righe partono a null e ognuna ha la sua lunghezza. Nel codice gestionale è quasi sempre la forma giusta, perché i dati veri sono irregolari: ogni ordine ha un numero diverso di righe.",
      "Tutte le copie predefinite in .NET sono shallow: Clone, ToArray, ToList e Array.Copy duplicano le caselle e condividono gli oggetti dentro. Per una copia profonda me la scrivo io, oppure rendo immutabili gli elementi.",
      "Contains su un array o su una List è una scansione lineare: dentro un ciclo diventa un O(n²) accidentale, invisibile con cento elementi e fatale con cinquantamila. Costruisco un HashSet una volta sola fuori dal ciclo.",
    ],
    keep: ["array", "Length", "GetLength", "rettangolare", "jagged", "shallow copy", "deep copy", "HashSet", "IReadOnlyList", "Span", "foreach", "RemoveAll"],
  },
  "02j-enums": {
    say: [
      "Un enum è un value type con sotto un intero e i nomi attaccati a compile time: per questo il cast funziona in entrambi i sensi e non costa niente a runtime.",
      "I valori li assegno sempre esplicitamente: con la numerazione implicita, inserire un membro in mezzo rinumera tutti quelli dopo, e le righe già salvate nel database non si spostano — quindi un ordine che era \"Picked\" diventa un altro stato senza che niente fallisca.",
      "Una variabile enum non è limitata ai suoi nomi: un cast da int non è controllato, quindi valido con Enum.IsDefined qualunque valore arrivi da fuori e metto sempre un ramo default che lancia, perché il C# non sa verificare l'esaustività.",
      "L'attributo Flags non fa quasi niente: cambia solo come stampa ToString. Quello che fa funzionare un enum di flag è che i valori siano potenze di due — e nessuno lo controlla per te.",
      "Su un'API serializzo sempre il nome e non il numero, altrimenti la mia numerazione interna diventa parte del contratto di qualcun altro e un refactoring interno diventa un breaking change silenzioso.",
    ],
    keep: ["enum", "underlying type", "IsDefined", "TryParse", "switch expression", "default", "Flags", "HasFlag", "potenze di due", "breaking change", "serializzazione"],
  },
  "02k-files-and-folders": {
    say: [
      "I path li costruisco sempre con Path.Combine: un backslash scritto a mano è il motivo più comune per cui del codice che funziona sul portatile muore appena viene messo in un container Linux.",
      "ReadLines e non ReadAllLines, a meno che non sappia che il file è piccolo: il primo è lazy e legge a memoria costante, il secondo carica tutto. È una lettera di differenza e la differenza fra funzionare sul file di test e reggere un export da quattro milioni di righe.",
      "Ogni stream vuole un using, altrimenti l'handle del sistema operativo resta aperto e il file resta bloccato finché non passa il garbage collector — e chi scrive dopo di me fallisce con \"il file è in uso\".",
      "Per scrivere un file che nessuno possa leggere a metà, scrivo su un nome temporaneo nella stessa cartella e poi rinomino: il rename dentro lo stesso volume è atomico, quindi il file appare completo o non appare.",
      "Numeri e date su file sempre con InvariantCulture, in scrittura e in lettura: 18.40 scritto in italiano diventa 18,40 e riletto in inglese diventa 1840, senza nessuna eccezione e con tutto quello che sta a valle sbagliato.",
    ],
    keep: ["Path.Combine", "ReadLines", "stream", "using", "handle", "FileShare", "IOException", "atomico", "rename", "encoding", "UTF-8", "BOM", "InvariantCulture", "round-trip"],
  },
  "02l-exceptions": {
    say: [
      "Le eccezioni sono per l'eccezionale: se chi chiama poteva ragionevolmente aspettarsi quell'esito — non trovato, password sbagliata, file non ancora arrivato — è un valore di ritorno, non un throw.",
      "throw; e mai throw ex;: il secondo resetta lo stack trace alla riga in cui è scritto, quindi il log punta al mio blocco catch invece che al punto in cui il problema è successo davvero. Ed è l'unica informazione che avevo.",
      "Un catch vuoto è peggio di un crash: non toglie il fallimento, toglie la segnalazione — l'API risponde 200 per un ordine che non è mai stato salvato, e il problema salta fuori giorni dopo senza niente nei log.",
      "Catturo il tipo più specifico possibile, e catch (Exception) sta in cima all'applicazione e quasi da nessun'altra parte: più in basso si porta via anche la OperationCanceledException, che è un utente che ha cambiato pagina e non un errore.",
      "Un'eccezione personalizzata la scrivo solo se qualcuno la catturerebbe davvero in modo specifico; la derivo dalla base più vicina, le do i dati come proprietà, e prevedo sempre l'overload con inner, perché quando traduco un errore la causa vera deve restare attaccata.",
    ],
    keep: ["eccezione", "throw", "rethrow", "stack trace", "inner exception", "exception filter", "finally", "using", "TryParse", "InvalidOperationException", "OperationCanceledException", "middleware", "ProblemDetails"],
  },
  "03b-classes-objects-members": {
    say: [
      "I campi sono sempre privati e lo stato si espone con le proprietà: una proprietà è una coppia di metodi travestita da dato, quindi può validare, calcolare, essere virtuale o stare su un'interfaccia — e trasformare un campo pubblico in proprietà è un breaking change binario.",
      "Il costruttore è il punto in cui l'oggetto diventa valido, ed è l'unico che possa garantirlo: se valido lì nessuna istanza può esistere rotta, se valido nel chiamante mi sto fidando di tutti i chiamanti, anche quelli non ancora scritti.",
      "Non chiamo mai un metodo virtual dentro un costruttore: il costruttore base gira prima che i campi della classe derivata siano inizializzati, quindi l'override trova tutto a null.",
      "Una collezione non la espongo mai come List: anche con la sola get il chiamante non può sostituirla ma può fare Add, quindi scavalca il metodo che contiene le regole. Campo privato readonly e IReadOnlyList in uscita.",
      "Parto da private e giustifico ogni passo verso l'alto: nelle applicazioni la maggior parte delle classi dovrebbe essere internal, perché è dettaglio implementativo, e InternalsVisibleTo dà comunque accesso ai test.",
    ],
    keep: ["campo", "proprietà", "auto-property", "init", "required", "const", "static readonly", "costruttore", "chaining", "primary constructor", "access modifier", "internal", "incapsulamento", "record", "struct"],
  },
  "03c-inheritance-and-interfaces": {
    say: [
      "Il polimorfismo è una chiamata risolta a runtime dal tipo reale dell'oggetto: l'overload invece si risolve a compile time dal tipo dichiarato, ed è la confusione più comune su questo argomento.",
      "override sostituisce, new nasconde: un membro nascosto segue la variabile e non l'oggetto, quindi la stessa istanza risponde in modo diverso a seconda di come la tengo — e non è praticamente mai quello che si voleva.",
      "L'ereditarietà vuol dire \"è un\", non \"usa\": se sto derivando per arrivare a un metodo, quello che volevo era una dipendenza nel costruttore. Derivare da un helper lo salda addosso al servizio, ne espone i membri e brucia l'unica classe base che ho.",
      "Un override che lancia NotSupportedException è un errore di design: la sottoclasse non può mantenere la promessa della base, quindi non è davvero un sottotipo — di solito sta modellando una fase e non un tipo di cosa.",
      "L'interfaccia è la giuntura che rende testabile l'applicazione: il servizio dipende da IOrderRepository, quindi il test gli passa l'implementazione in memoria e non tocca nessun database. La classe astratta la introduco solo quando c'è comportamento davvero condiviso da scrivere una volta sola.",
    ],
    keep: ["ereditarietà", "polimorfismo", "astrazione", "interfaccia", "classe astratta", "virtual", "override", "new", "sealed", "base", "template method", "Liskov", "sostituibilità", "composizione", "dependency injection"],
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
  "11b-oracle": {
    say: [
      "Con Oracle non ho lavorato come motore principale: uso SQL Server e PostgreSQL, ma i fondamentali relazionali sono gli stessi, quindi quello che mi manca è il dialetto, non una competenza nuova.",
      "Le differenze che conosco: VARCHAR2 invece di VARCHAR, NUMBER come unico tipo numerico, le sequence o GENERATED AS IDENTITY per le chiavi, il PL/SQL al posto delle stored procedure T-SQL, e soprattutto che su Oracle una stringa vuota è NULL.",
      "La differenza che conta davvero non è di sintassi ma di comportamento: di default Oracle usa la lettura consistente a più versioni, quindi chi legge non blocca chi scrive — per quello su Oracle non serve il riflesso del NOLOCK che si usa su SQL Server.",
      "Su Oracle devo ricordarmi il COMMIT esplicito e che il DDL fa commit da solo: due cose che sorprendono chi arriva dall'autocommit di SQL Server.",
      "Da .NET cambia poco: in EF Core è UseOracle al posto di UseSqlServer, le entità e le query LINQ restano; quello che emerge sono i nomi, i tipi e la regola della stringa vuota.",
    ],
    keep: ["Oracle", "VARCHAR2", "NUMBER", "sequence", "PL/SQL", "NULL", "NOLOCK", "MVCC", "COMMIT", "DDL", "UseOracle", "ROWNUM"],
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
  "18b-angular": {
    say: [
      "Un componente Angular è una classe TypeScript più un template: la classe tiene lo stato, il template si lega ai suoi valori e Angular ridisegna solo quello che è cambiato.",
      "I servizi si iniettano come in ASP.NET Core, e HttpClient restituisce un Observable: finché nessuno fa la subscribe, la richiesta non parte.",
      "Non sono uno specialista frontend: so leggere un componente, seguire i dati fino al mio endpoint e sistemare la schermata che alimenta la mia API.",
    ],
    keep: ["component", "template", "service", "dependency injection", "HttpClient", "Observable", "subscribe", "binding", "interceptor", "guard", "CORS"],
  },
  "18c-react": {
    say: [
      "Un componente React è una funzione: riceve le props e restituisce quello che va disegnato. Quando lo stato o le props cambiano, React la richiama, confronta il risultato con quello precedente e aggiorna solo le parti diverse.",
      "La funzione deve essere pura: stessi dati in ingresso, stesso risultato, nessun effetto collaterale. Tutto quello che tocca il mondo esterno — una chiamata HTTP, un timer, una subscription — va dentro una useEffect, con la sua cleanup.",
      "I dati che arrivano dal server non sono stato dell'applicazione: sono una copia in cache di qualcosa che possiede l'API. Li tengo in una query cache, che si occupa di staleness, refetch e cancellazione.",
      "Ogni riga di una lista ha bisogno di una key stabile: con l'indice dell'array, se cancelli una riga, tutte quelle sotto cambiano identità.",
    ],
    keep: ["component", "props", "state", "hook", "useState", "useEffect", "dependency array", "cleanup", "stale closure", "key", "render", "query cache", "context"],
  },
  "19b-realtime": {
    say: [
      "Con il polling è il client che chiede a intervalli: semplice, ma la latenza è in media metà dell'intervallo e le richieste si pagano anche quando non è cambiato niente. Con un WebSocket la connessione resta aperta e il server può parlare per primo.",
      "SignalR non è un protocollo diverso: è una libreria sopra WebSocket, Server-Sent Events e long polling, che aggiunge i gruppi, la riconnessione automatica e il backplane.",
      "Appena metti due istanze dietro un load balancer serve un backplane, altrimenti chi è connesso alla seconda istanza non riceve niente — senza nessun errore.",
      "Un hub non è una coda: quello che è stato inviato mentre eri disconnesso è perso. Alla riconnessione si rilegge lo stato, non si chiede il replay, e si rientra nei gruppi perché il connection id è nuovo.",
    ],
    keep: ["polling", "WebSocket", "Server-Sent Events", "long polling", "SignalR", "hub", "group", "connection id", "backplane", "sticky session", "reconnect", "keep-alive", "snapshot"],
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
  "22c-distributed-state": {
    say: [
      "La consistenza eventuale garantisce che, se le scritture si fermano, tutte le copie convergono sullo stesso valore. Non garantisce niente su un istante preciso: due utenti possono legittimamente vedere risposte diverse nello stesso momento, e il lavoro è decidere cosa mostrare a ciascuno nel frattempo.",
      "Il default giusto resta un solo nodo che scrive con delle repliche in lettura: c'è un solo ordine degli eventi e i conflitti non esistono. Il prezzo è il ritardo di replica, per cui un utente può rileggere quello che ha appena scritto e non vederlo.",
      "Il conflitto migliore è quello che non può succedere: assegno la proprietà delle righe a un sito solo, e gli altri le hanno in sola lettura. Il last write wins lo uso solo dove perdere una modifica non fa danno, perché gli orologi di due macchine non coincidono.",
      "Per il lavoro offline ogni dispositivo scrive in locale più un outbox, con gli id generati dal client. Alla riconnessione la coda si svuota in ordine, ogni modifica porta una chiave di idempotenza e la versione su cui è stata fatta, e la risposta del server sovrascrive la copia locale.",
      "Alta disponibilità vuol dire prima di tutto due numeri: RTO, quanto tempo posso restare fermo, e RPO, quanti dati posso perdere. Sono quelli che decidono se la replica è sincrona o asincrona.",
    ],
    keep: ["replica", "replication lag", "multi-master", "conflict", "last write wins", "version vector", "CRDT", "eventual consistency", "CAP", "idempotente", "outbox", "offline", "failover", "split-brain", "quorum", "RTO", "RPO"],
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
  "32-industrial-and-mes": {
    say: [
      "Un MES sta al livello 3 della piramide ISA-95: sotto c'\u00e8 la supervisione, sopra l'ERP. Si occupa di mandare in produzione gli ordini, contare pezzi buoni e scarti, registrare le causali di fermo, calcolare l'OEE e tenere la tracciabilit\u00e0.",
      "L'OEE \u00e8 disponibilit\u00e0 per prestazione per qualit\u00e0. Il punto per\u00f2 \u00e8 che le tre frazioni dipendono da definizioni concordate: decidere cosa conta come fermo pianificato sposta il numero di diversi punti senza toccare nulla in linea.",
      "Non ho esperienza di programmazione PLC e non me la attribuisco. Lavoro sopra il PLC: prendo i dati, tengo il registro, mando gli ordini e rispondo all'ERP.",
    ],
    keep: ["MES", "SCADA", "PLC", "HMI", "OPC UA", "ISA-95", "OEE", "batch", "lotto"],
  },
  "32b-talking-to-machines": {
    say: [
      "Con Modbus leggo un numero a un indirizzo: il protocollo non porta n\u00e9 il tipo n\u00e9 l'unit\u00e0 di misura, quindi la scalatura sta in un solo punto del codice, nella definizione del tag.",
      "Preferisco una subscription al polling: un fermo di sessanta millisecondi non lo vedo interrogando una volta al secondo, e il polling pesa sul PLC, non su di me.",
      "OPC UA porta nome, tipo, qualit\u00e0 e due timestamp: quello della macchina e quello del server. La differenza tra i due \u00e8 la latenza, ed \u00e8 la prima cosa su cui metterei un allarme.",
      "Un valore con qualit\u00e0 Uncertain lo salvo con la sua qualit\u00e0 e lo escludo dalle medie: registrarlo come zero sporca il dato e non se ne accorge nessuno.",
    ],
    keep: ["Modbus", "OPC UA", "MQTT", "tag", "polling", "subscription", "timestamp", "quality", "deadband", "gateway", "backpressure"],
  },
  "32c-wcs-traffic-and-commissioning": {
    say: [
      "Il traffic manager assegna le zone tutte insieme o nessuna: cos\u00ec un veicolo non ne tiene mai solo una parte, e il deadlock non si pu\u00f2 proprio formare.",
      "In campo non esiste il timeout: due veicoli fermi uno davanti all'altro in corsia ci restano fino a domattina, quindi il deadlock va prevenuto per costruzione, non gestito dopo.",
      "La dimensione delle zone \u00e8 la leva sulla produttivit\u00e0: zone troppo grandi mettono in fila movimenti che non si darebbero fastidio a vicenda.",
      "So che gran parte del lavoro vero \u00e8 il collaudo e la messa in servizio in cantiere. Posso chiedere quante settimane di trasferta sono previste in un anno?",
    ],
    keep: ["WCS", "AGV", "LGV", "deadlock", "throughput", "OEE", "FAT", "SAT", "commissioning"],
  },
  "32d-the-edge-and-the-advert": {
    say: [
      "Un flusso seriale non ha messaggi: la Read restituisce i byte arrivati, non una trama. Accumulo in un buffer che vive tra una lettura e l'altra, cerco le trame complete e tengo il resto.",
      "RS-232 è punto-punto; RS-485 è un bus multi-drop half duplex, quindi parla uno alla volta e il silenzio dopo la richiesta fa parte del protocollo. È per questo che Modbus RTU è fatto così.",
      "Sul CAN non ci sono indirizzi: l'identificatore dà il nome al messaggio e ne stabilisce la priorità, e vince sempre quello più basso. Una trama a bassa priorità non ha un tempo di consegna garantito.",
      "Windows non è un sistema real-time: i tempi duri stanno nel PLC o nel microcontrollore. Io lavoro sopra, e progetto perché il mio caso peggiore non fermi la linea.",
      "Non ho mai scritto firmware su microcontrollore in C: scrivo il software che ci parla — seriale, Modbus, CAN, OPC UA. Preferisco dirlo subito, così il resto di quello che dico vale.",
    ],
    keep: ["seriale", "RS-232", "RS-485", "Modbus RTU", "CAN", "firmware", "real-time", "PLC"],
  },

  "33b-ai-in-dotnet": {
    say: [
      "Per me una chiamata a un modello è una dipendenza esterna inaffidabile: timeout, retry con jitter, circuit breaker e un comportamento definito per quando non risponde. Vale esattamente quello che vale per una qualsiasi integrazione.",
      "Il modello non è deterministico, quindi non decide mai un'invariante di dominio: sta dietro un'interfaccia in infrastructure, propone e non dispone. E se mando la mail di un cliente a un servizio esterno è un trasferimento di dati personali, quindi servono una base giuridica e una region europea.",
    ],
    keep: ["LLM", "prompt", "token", "embedding", "RAG", "retry", "circuit breaker", "hallucination", "prompt injection", "GDPR"],
  },
  "33c-crm-and-odata": {
    say: [
      "Un'integrazione con un CRM non è un problema di trasporto, è un problema di proprietà del dato: prima si decide chi è il sistema di riferimento campo per campo, qual è la chiave che dice che è la stessa azienda, e cosa vuol dire una cancellazione da una parte e dall'altra.",
      "In genere il CRM è padrone del dato commerciale e il gestionale del dato fiscale — partita IVA, fido, indirizzo di fatturazione. Dire soltanto «il CRM è il master» non è una decisione, è rimandarla.",
      "Scrivo sempre con un upsert sulla mia chiave — chiave alternativa su Dataverse, campo External Id su Salesforce — così la chiamata è idempotente e rilanciare il batch dopo un errore non crea duplicati.",
      "Sull'OData metto sempre il $select e seguo l'@odata.nextLink finché c'è: Dataverse taglia la pagina a cinquemila righe comunque, quindi chi non lo segue sincronizza una parte dei dati e dice che è andato tutto bene.",
      "Il 429 con Retry-After non è un errore, è controllo di flusso: aspetto esattamente quel tempo e poi raggruppo le scritture, invece di fare una chiamata per riga.",
      "Preferisco il polling se non c'è una persona che aspetta il risultato. Con il webhook la politica di retry del loro sistema diventa un mio requisito di disponibilità, e comunque l'evento lo tratto come un campanello: rileggo il record aggiornato, non applico il payload.",
    ],
    keep: ["CRM", "Salesforce", "Dynamics 365", "Dataverse", "OData", "upsert", "External Id", "chiave alternativa", "delta", "webhook", "polling", "idempotente"],
  },

  "34-agile-and-scrum": {
    say: [
      "Nello Scrum lavoriamo per sprint; nel daily dico cosa ho chiuso, cosa faccio oggi e se sono bloccato — non un resoconto minuto per minuto.",
      "Se una stima si rivela sbagliata lo dico subito: il problema non è sbagliare la stima, è dirlo l'ultimo giorno.",
    ],
    keep: ["sprint", "backlog", "daily", "retrospettiva", "story point", "stand-up"],
  },
};
