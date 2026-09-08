# Module 17 — Landing a .NET job in Modena and Bologna

> The technical modules make you employable. This one makes you *hired in a specific place*, which
> is a different problem with different rules.

**A note on this module's reliability.** Salary bands, hiring conditions and immigration rules
change, and I cannot check live listings for you. Treat every number here as **an order of magnitude
to calibrate against, not a quote** — verify on LinkedIn Salary, Glassdoor and actual job adverts
before you negotiate anything. The structural facts (contract types, CCNL, the CV convention, how
the market is shaped) are stable. The numbers are not.

---

## 1. Why this is a good region to want a .NET job in

Emilia-Romagna is one of the densest industrial areas in Europe, and its industry is the kind that
buys and builds software:

| Cluster | Where | What it needs software for |
|---|---|---|
| **Motor Valley** — automotive and motorsport | Modena, Maranello, Sant'Agata Bolognese, Borgo Panigale | Production traceability, configurators, test-bench data, quality |
| **Packaging Valley** — automatic machinery | Bologna, Ozzano, Pianoro, Imola | Machine control, MES, line supervision, remote service |
| **Ceramics** | Sassuolo, Fiorano (Modena) | Plant automation, kiln/line monitoring, logistics |
| **Biomedical** | Mirandola (Modena) | Traceability, regulated quality systems, validation |
| **Agrifood** | across the region | Lot traceability, cold chain, ERP integration |
| **Services, data, research** | Bologna | Fintech and credit data, utilities, logistics, the Bologna Tecnopolo data/supercomputing hub, the university |

**Why this matters for you specifically:** manufacturing IT in Italy is disproportionately Microsoft.
SQL Server, Windows on the shop floor, decades of WinForms and WPF, Excel everywhere, and — this is
the important part — **a large installed base now being migrated to ASP.NET Core and Blazor.** That
migration is where the jobs are. You are not competing to be the tenth React developer at a startup;
you are offering to help move a fifteen-year-old `gestionale` onto something maintainable.

Bologna and Modena are ~40 minutes apart by train, and Modena–Bologna–Reggio–Parma functions as one
labour market. **Search all of it.** Restricting to one city cuts your options roughly in half for
no benefit.

```
   ◄──────────────────── one labour market, end to end about an hour ────────────────────►

   PARMA ─────── REGGIO ─────── MODENA ══════════════ BOLOGNA ─────── IMOLA ─────── (Ravenna)
   agrifood      agrifood          │                      │             │
                                   │                      │             └ machinery, motorsport
                 Sassuolo · Fiorano┤                      ├ Borgo Panigale — motor
                   ceramics        │                      ├ Ozzano · Pianoro — packaging
                 Maranello ────────┤                      ├ Tecnopolo — data, HPC, research
                   motor           │                      └ services, fintech, logistics
                 Mirandola ────────┘
                   biomedical

   search all of it. Restricting yourself to one city halves your options for no benefit.
```

---

## 2. The five employer archetypes

You will be interviewing with one of these. They want different things and pay differently.

**1. The machinery / manufacturing company's internal IT.**
Ferrari, Ducati, IMA, Marchesini, Sacmi, System Ceramics, Bonfiglioli, Datalogic and the hundreds of
less famous suppliers around them. Stable, well-paid by regional standards, often **Italian-speaking**,
usually a mix of legacy maintenance and new development. Expect SQL Server, ERP integration, and
software that talks to machines.
→ *They value:* reliability, SQL, understanding their business. Not framework fashion.

**2. The software house / system integrator selling to those companies.**
The largest single category and your most likely first job. Project work, multiple clients, faster
learning, more pressure. Often involves **trasferte** — travel to customer sites for installation and
commissioning. Ask about this in the first interview; it is a genuine lifestyle question, not a
detail.
→ *They value:* breadth, being presentable to a client, shipping.

**3. The ERP / `gestionale` vendor and its partner network.**
Italy has a large domestic business-software industry, and a very large **Microsoft Dynamics 365
Business Central** partner ecosystem. Adjacent to that sits Italian **fatturazione elettronica** —
electronic invoicing through the SDI, mandatory for Italian businesses, which generates a permanent
stream of .NET integration work. Unglamorous, in constant demand, and a genuinely reliable way in.
→ *They value:* accounting/logistics domain knowledge, XML, integration patience.

**4. Consultancies and multinationals.**
Engineering, Reply, Accenture, NTT Data, Lutech, Var Group and similar, all with a regional presence.
Structured hiring, clear levels, more English, more likely to hire someone without a local network,
and the most realistic entry point if your Italian is weak.
→ *They value:* certifications, clean fundamentals, the ability to be billed to a client.

**5. Product companies and scale-ups.**
Fewer, mostly Bologna. Fintech and credit data, e-commerce, logistics platforms, health tech.
Best English, most modern stack, most competitive.

> **Verify, do not trust this list.** I am naming well-known companies in the area to show you the
> *shape* of the market — I have not checked any individual company's current stack or openings.
> Read their careers pages.

---

## 3. What they actually ask for, ranked by how much it matters

After this course you will be strong on 1, 2 and 4. Be honest with yourself about 3, 5 and 6.

| # | Skill | Where you get it |
|---|---|---|
| 1 | **SQL Server, properly** — joins, indexes, execution plans, transactions, isolation levels | Module 07. **Do not skip this.** It is the single most reliably tested skill in this market and the one candidates are weakest at |
| 2 | **C# + .NET, current version** | Modules 01–04 |
| 3 | **ASP.NET Core Web API** | Modules 10 and 15 |
| 4 | **EF Core**, and knowing when to drop to Dapper or raw SQL | Modules 06, 09, 16 |
| 5 | **Something with a UI** — Blazor, or WPF/WinForms for the industrial world | **Module 18**, which builds the Blazor front end over LogiFlow. It is a weekend, and it materially widens your options in this market |
| 6 | **Git, Docker, CI** | Modules 00, 13 |
| 7 | **Azure or on-prem ops** | Module 13. Note that manufacturing is often still on-premises — do not assume cloud |
| 8 | **Industrial context** — MES, SCADA, WCS, PLC, OPC UA | **[Module 28](../module-28-industrial-and-ot/)**, and [site chapter 32](../../site/chapters/32-industrial-and-mes.html). Ranked eighth for the region as a whole and **far higher than that** along the Sassuolo–Fiorano–Viano belt, where intralogistics and machine builders hire .NET people continuously — for those employers it outranks Azure |
| 9 | **Legacy .NET Framework** — `Global.asax`, `web.config`, WebForms, IIS pools, and migrating incrementally rather than rewriting | [Site chapter 16b](../../site/chapters/16b-aspnet-framework.html). Adverts that list *ASP.NET* and *ASP.NET Core* as two skills, or name 4.8 by version, are describing their repository |

**The unfair advantage available to you:** almost nobody applying at junior or mid level can explain
*why* a query is slow, or what an execution plan is telling them. Module 07 plus module 16 puts you
in a small minority. Lead with it.

**The second one, and it is larger than it looks:** be the candidate who will willingly open the
2011 solution. Every bootcamp graduate you are competing against can do greenfield ASP.NET Core;
far fewer can read a `Global.asax` without flinching, and almost none can explain why `.Result`
deadlocks on Framework and merely wastes a thread on Core. Meanwhile a great deal of the revenue in
this region still runs on .NET Framework 4.8, and somebody has to keep it alive and move it
forward — which, for a junior, is very often the actual job on offer.

So do not present legacy work as a compromise. *"I am comfortable in both, and I would migrate
incrementally behind a proxy rather than propose a rewrite"* is a sentence that separates you from
the field, because it says you have thought about their constraints rather than your preferences.

---

## 4. Italian

The honest position:

| Employer type | Realistic requirement |
|---|---|
| Manufacturing internal IT, smaller software houses | **Italian B1–B2 effectively required.** Daily work is in Italian, with colleagues who do not speak English |
| ERP / `gestionale` vendors | Italian required — the domain vocabulary *is* Italian |
| Large consultancies, multinationals | English often sufficient to start; Italian expected to grow |
| Bologna product companies | English frequently fine |

**If you are not yet at B1, start today and put "Italian: A2, in active study" on your CV.** Showing
a trajectory is far better than silence, and it answers the question every recruiter is silently
asking. If you are already B2, say so at the top — in this region it is worth more than a
certification.

Two practical notes: technical terms are largely borrowed from English (`deploy`, `commit`,
`branch`, `query`), so technical conversation is easier than social conversation. And meetings will
be faster and more idiomatic than any Italian course prepares you for.

---

## 5. The CV, Italian conventions

- **Two pages maximum.** PDF. Named `CV_Surname_Name.pdf`.
- **Photo:** conventional in Italy and still expected by many recruiters, though attitudes are
  shifting. Neutral either way — include one if you are comfortable.
- **Include the GDPR authorisation line.** Its absence is noticed; some systems filter on it:
  > *Autorizzo il trattamento dei miei dati personali ai sensi del Regolamento UE 2016/679 (GDPR).*
- **State your work authorisation explicitly** if you are not an EU citizen. Recruiters discard
  ambiguity rather than investigate it. One line: "Cittadino UE" or "Permesso di soggiorno valido,
  autorizzato a lavorare" or "Necessita di sponsorship".
- **Write it in Italian** for Italian-speaking employers, even if imperfectly, and keep an English
  version for the multinationals. A slightly awkward Italian CV signals commitment; a monolingual
  English one signals you have not thought about the move.
- **Lead with the project, not the education.** A public repository with a real architecture beats a
  list of courses, and for a career changer it beats almost everything else.
- **Do not use the Europass template** for a technical role. It is verbose and dated. A clean
  one-column CV is better.

---

## 6. Money and contracts

Compensation in Italy is quoted as **RAL** — *retribuzione annua lorda*, gross annual, before tax,
normally spread over **13 or 14 monthly payments** (the *tredicesima* and sometimes *quattordicesima*).
"€35.000" means gross per year, not net, and net will be roughly 60–70% of it depending on
circumstances.

**Approximate RAL bands, Emilia-Romagna, .NET roles — calibration only, verify before negotiating:**

| Level | Rough RAL |
|---|---|
| Junior / first role (0–2 yrs) | €25,000 – €32,000 |
| Mid (3–5 yrs) | €33,000 – €42,000 |
| Senior (6+ yrs) | €45,000 – €58,000 |
| Lead / architect | €55,000 – €70,000+ |

```
   RAL €35.000  ──►  gross, per year, before tax
                ──►  paid over 13 or sometimes 14 months (tredicesima, quattordicesima)
                ──►  net is roughly 60–70% of it, depending on circumstances
                ──►  and then: buoni pasto · welfare aziendale · smart working days ·
                     training budget · WHICH CCNL — all of which are negotiable, and all
                     of which candidates forget to ask about
```

Milan runs meaningfully higher; the region compensates with a much lower cost of living. Fully
remote roles for foreign companies are the main way to break the ceiling, and are a different job
search.

**Contract types you will be offered:**

| Italian | What it is | Watch for |
|---|---|---|
| **Tempo indeterminato** | Permanent. The goal. | Includes a **periodo di prova** (probation), typically weeks to months |
| **Tempo determinato** | Fixed-term | Normal as a first step; ask what conversion looks like |
| **Apprendistato** | Apprenticeship, generally under 30 | Lower pay, but cheap for the employer — which makes it a real *opening* for a career changer. Includes training obligations |
| **Somministrazione** | Via a staffing agency | You are employed by the agency, placed at the client |
| **Stage / tirocinio** | Internship, with a small allowance | Reasonable for a genuine career change; not reasonable if you already have experience |
| **Partita IVA** | You invoice as a freelancer | **Be careful.** Legitimate for real consulting. But if you work fixed hours at one client's office with their equipment, that is *false partita IVA* — you carry the tax and social-security burden and have none of the protections. For a first job in a new country, prefer employment |

Also normal and worth asking about: **buoni pasto** (meal vouchers, untaxed up to a limit),
*smart working* days, *welfare aziendale*, training budget, and which **CCNL** applies —
*Metalmeccanico* (industry) and *Commercio/Terziario* differ in holidays and minimums.

---

## 7. Where to look, and how

**Boards:** LinkedIn (dominant — set alerts for `.NET` / `C#` within 50 km of Bologna), InfoJobs
(strong in Italy, used heavily by SMEs), Indeed IT, Glassdoor, and **AlmaLaurea** if you have any
Italian university affiliation.

**Recruiters and staffing agencies** are more central to Italian hiring than in many countries — Hays,
Michael Page, Randstad Technologies, Experis/ManpowerGroup, Adecco, Gi Group. Register with two or
three. They will call.

**The channel most people neglect:** apply directly through company career pages. Industrial
companies in this region often post there first, or only. Build a list of 40 companies across the
clusters in §1 and work through it.

**Search in Italian.** `sviluppatore .NET`, `programmatore C#`, `sviluppatore software Bologna`,
`analista programmatore`. That last one is a very common Italian job title with no clean English
equivalent — roughly developer-with-analysis-responsibility.

**A note on `annunci`:** Italian job ads often list an implausible number of technologies. Apply at
around 60% match. The list is a wish, not a filter.

---

## 8. If you are not an EU citizen

I do not know your situation, so both paths, briefly — and this is the area where you should trust
official sources over me, because it changes and the details matter enormously.

- **EU/EEA/Swiss citizen:** you may work immediately. Register residency at the *comune*, get a
  **codice fiscale**, open an Italian bank account, and set up **SPID** (digital identity) — you will
  need it constantly.
- **Non-EU:** the general employment route runs through quotas (**decreto flussi**) and an employer
  obtaining a **nulla osta**, which is slow and off-putting to smaller employers. The route worth
  investigating first is the **EU Blue Card** (*Carta Blu UE*) for highly qualified workers, which
  sits outside those quotas and whose requirements were relaxed by Italy's 2023 implementation of the
  revised EU directive — including, in some cases, accepting relevant professional experience in ICT
  in place of a degree. A third common path is studying in Italy (Bologna and Modena both have strong
  universities) and converting a student permit to a work permit.

  **Verify current thresholds and conditions with an official source or an immigration lawyer before
  planning around any of this.** Then, practically: target archetypes 4 and 5 from §2 — larger
  employers have done this before and have HR who know the process. Small manufacturers usually have
  not, and will not start for a first hire.

---

## 9. The interview, and how to use this repository in it

Typical shape: a recruiter or HR call, then a technical interview (often a conversation rather than
live coding — Italian technical interviews lean toward discussion and past work), sometimes a take-home
or a whiteboard session, then a meeting with management to discuss RAL and contract.

**Bring LogiFlow.** A career changer with a public repository containing Clean Architecture, CQRS, a
transactional outbox, optimistic concurrency, architecture tests and integration tests against a
real database is not a typical junior applicant. But you must be able to defend every decision in it, including the ones the README
already admits are compromises — *that* is the part that reads as senior.

**Make it yours before you send it.** Fork it and re-skin the domain to match the region: production
orders instead of sales orders, work centres, machine downtime events, lot traceability. It is
mostly renaming, and the effect in an interview at a Packaging Valley company is completely
different. You stop being someone who did a tutorial and start being someone who understood their
problem.

**Prepare these five answers.** They come up constantly:

1. Why is this query slow, and how would you find out? *(execution plan, indexes, N+1 — module 07)*
2. `IEnumerable` vs `IQueryable`. *(module 16 — the single best discriminator in this market)*
3. How do you stop two users overselling the last unit of stock? *(module 07)*
4. Explain the middleware pipeline and why order matters. *(module 15)*
5. Why did you separate Domain from Infrastructure? *(module 05 — and be able to name the cost)*

**Ask them:** What does the legacy estate look like, and what is the migration plan? Who owns the
database? Is there a test suite? How much trasferta? Which CCNL?

Questions like these mark you as someone who has worked on real systems, and their answers tell you
whether the job is one you want.

---

## 10. Italian you will actually need

Technical Italian is largely English. **Business-domain Italian is not**, and this vocabulary is what
lets you follow a requirements meeting at a manufacturing company.

| Italian | Meaning |
|---|---|
| **gestionale** | The business management system / ERP. You will hear this constantly |
| **commessa** | Job order / customer project |
| **anagrafica** | Master data (customers, products, suppliers) |
| **distinta base** | Bill of materials (BOM) |
| **magazzino** / **giacenza** | Warehouse / stock on hand |
| **ordine di produzione** | Production order |
| **ciclo di lavorazione** | Routing, the sequence of operations |
| **reparto** | Department, shop-floor area |
| **collaudo** | Acceptance testing |
| **fatturazione elettronica**, **SDI** | E-invoicing and the national exchange system |
| **DDT** (*documento di trasporto*) | Delivery note |
| **preventivo** | Quote |
| **scadenzario** | Due-date / payment schedule |
| **capitolato** | Specification document, usually contractual |
| **fornitore** / **cliente** | Supplier / customer |
| **trasferta** | Business travel to a customer site |
| **colloquio conoscitivo / tecnico** | Introductory / technical interview |
| **assunzione** · **periodo di prova** · **ferie** | Hiring · probation · holidays |

---

## 11. A twelve-week plan

Assumes evenings and weekends. Compress it if you have full days.

| Weeks | Technical | Career, in parallel |
|---|---|---|
| **1–2** | Module 16 (skim), then 00–04. Get the app running. | Italian: start or restart. CV drafted in both languages |
| **3–4** | Modules 05, 06, **07**. Do module 07 properly — twice. | Build the 40-company list. Register with 2–3 recruiters |
| **5–6** | Modules 08, 09, **15** | **Start applying.** Do not wait until you feel ready; pipelines take weeks. Early interviews are practice |
| **7–8** | Modules 10, 11, 12. Make the labs green. | Re-skin LogiFlow to a manufacturing domain. Publish it |
| **9** | Modules 13, 14, **18** | Ship the Blazor UI (module 18) and link it from the CV |
| **10** | Re-read module 16. Rehearse the five answers aloud | Interviews. Follow up on every application |
| **11–12** | Fill your weakest gap — usually SQL or the UI layer | Negotiate. Compare RAL, contract type, CCNL and trasferta, not just the number |

```
   week      1    2    3    4    5    6    7    8    9   10   11   12
   ─────────────────────────────────────────────────────────────────────────────────────
   technical 16,00─04   05,06,07   08,09,15   10,11,12  13,14,18  revise   fill the gap
   Italian   ████████████████████████████████████████████████████████████████████████ every day
   CV / list ██████████
   applying                 ▲ ═══════════════════════════════════════════════ negotiate
                            └ week 5. Not week 12.

   the two tracks run in PARALLEL, because the job search is the slower of the two:
   pipelines take weeks, and the early interviews are practice you cannot get any other way.
```

**The one non-negotiable:** applications start in week 5, not week 12. The technical work and the job
search run in parallel, because the job search is the slower of the two.

---

## 12. Golden rules

> The card. Technique is the other twenty-eight modules; this is the part candidates get wrong.

1. **Search the whole Modena–Bologna corridor**, not one city. It is one labour market and it is
   forty minutes wide.
2. **Lead with SQL.** Almost nobody at junior or mid level can explain why a query is slow or read
   an execution plan. Module 07 puts you in a small minority — say so early.
3. **Apply at about 60% match.** Italian job adverts list a wish, not a filter.
4. **Applications start in week 5, not week 12.** Pipelines take weeks; the technical work and the
   search run in parallel or you finish the course unemployed.
5. **Put your Italian on the CV as a trajectory.** "Italiano: A2, in studio attivo" beats silence,
   and answers the question every recruiter is silently asking.
6. **State your work authorisation in one line.** Recruiters discard ambiguity rather than
   investigate it.
7. **Two pages, PDF, the GDPR line, no Europass, project before education.** For a career changer,
   a public repository with a real architecture beats almost everything else on the page.
8. **Re-skin LogiFlow to the local domain before you send it** — production orders, work centres,
   lot traceability. Mostly renaming, and it changes what you are in the room.
9. **Be able to defend every decision in it, including the compromises the README admits to.**
   Naming the condition under which you would *not* do something is what reads as senior.
10. **Prefer employment to `partita IVA` for a first job here.** Fixed hours at one client's office
    with their equipment is not freelancing; it is the burden without the protection.
11. **Ask about trasferta, the legacy estate, who owns the database, and which CCNL.** Their
    answers tell you whether you want the job; the questions tell them you have worked on real
    systems.
12. **Compare offers on RAL, contract type, CCNL, meal vouchers and travel — not on the number
    alone.** And verify every figure in this module before you negotiate: they move, and I cannot
    check them for you.

---

## Where to go from here

You now have the full stack this region hires for, and a map of the market. The remaining gap is
almost never technical knowledge — it is volume of applications and Italian.

*In bocca al lupo.*

---

## Next

→ [Module 18 — Blazor: a UI over the API](../module-18-blazor/)

The last module of Part IV, and the one that gives §3's fifth row an answer: something with a UI,
built over the API you already have. After it, **Part V goes underneath the language** — memory,
equality, threads and the CLR — which is where the depth an interviewer probes for actually lives.
