# The job itself

> Commissioning, `cantiere`, `trasferta`, FAT and SAT, and the 03:00 call. What the advert means
> by the sentences it does not explain, the Italian words you will hear on the first day, and the
> three questions to ask before you accept.

Nobody writes this chapter, and every candidate wishes somebody had. It is not about technology.
It is about what the work is actually like — and it exists because the single biggest reason people
leave these jobs is not the code, it is discovering the lifestyle after signing.

---

## 1. Reading the advert properly

Real postings from this region, and what the quiet sentences mean.

> *"Partecipare attivamente alle **attività di cantiere** fino al rilascio al cliente."*

`Cantiere` literally means a building site. Here it means the customer's plant while the system is
being installed. This sentence is the single most important one in the advert, and it is doing a
lot of work: it says that a meaningful part of your job happens **somewhere else, in person, until
the customer signs**. Not a week. Often weeks at a time, repeatedly, for the life of a project.

> *"Collaborare con l'ufficio tecnico software per la risoluzione di bug durante il
> **commissioning**."*

Commissioning is the phase where the physical system is assembled, powered, and made to work for
the first time. It is where your software meets the real machine, and where every assumption you
made in the office is tested at once, usually with the mechanical and electrical teams standing
next to you, on a schedule that is already late because construction always runs late.

> *"Disponibilità a **trasferte** in Italia e all'estero."*

Travel. This is the lifestyle question, and it is not a detail. Ask specifically: how many weeks a
year, how long is a typical trip, is it Europe or worldwide, and what happens at weekends. "Some
travel" covers everything from four days a year to twenty weeks, and the difference is your life.

> *"**Commessa**"* — the word that organises everything.

A `commessa` is a job order: one customer's system, sold, engineered, built and installed as a
project with a number. Everything at a machine builder or an intralogistics company is organised by
`commessa` — budgets, hours, deadlines, and your calendar. It is the reason this work feels like
project work rather than product work even when the software is the same product every time, and
it is why "we always do it this way" is followed by "except on this commessa".

---

## 2. The shape of a project

```
   sales/offer → engineering → build in the workshop → FAT → ship → install → SAT → handover → support
                     ↑                                  ↑                       ↑         ↑
                  you write                        you are here            you are    for years
                  most of the code                 for a week or two       here for
                                                                           weeks
```

**FAT — Factory Acceptance Test.** The system is assembled and tested *at your own factory*, with
the customer present, before it is shipped. This is the good version: your building, your tools,
your colleagues down the corridor, and a customer who is watching but not yet losing money.

**SAT — Site Acceptance Test.** The same demonstration at the customer's plant, on the real floor,
integrated with everything else they own. This is the one that matters commercially, because the
payment milestone is usually attached to it.

**Handover and warranty.** The system becomes the customer's. You are now support, and you will be
the person who knows this `commessa`, possibly for years, possibly after you have moved on to two
other projects.

**Why this matters for how you write code:** you will not be there. Someone will phone about this
system in four years, and it may not be you who answers. Every argument this course makes about
readable code and comments that explain *why* is worth twice as much here as it is in a product
team, because there is no "the team knows" — there is a `commessa` folder and whatever you wrote
down.

---

## 3. What the days are actually like

**In the office**, it is ordinary software work: C#, SQL Server, a supervisor service, a WPF or
Blazor screen, a database schema, a code review. Comfortable, and the part the interview mostly
tests.

**On site**, it is not. Some honest details:

- **The line is stopped while you work, or worse, it is running.** Either way somebody is counting.
- **You are the software person among mechanical and electrical people**, and the default assumption
  when something does not work is that it is the software. Frequently it is not — it is a sensor, a
  cable, a wrongly set parameter — and part of the skill is demonstrating that calmly and without
  making anyone defensive. The engineer who can say *"the tag is not changing, so let us look at the
  input card together"* is worth a great deal more than the one who says "not my problem".
- **The hours are what the schedule needs.** Nights and weekends happen, because the only time you
  can have the line is when it is not producing. This is usually compensated, and you should ask
  how.
- **Conditions are industrial.** Noisy, hot or cold, safety boots and a hi-vis, and in food plants
  possibly a hairnet and a hygiene induction.
- **The satisfaction is real, and people stay for it.** You watch a thing you wrote move several
  tonnes of product correctly, all day, for years. Very little software work gives you that.

**And then the 03:00 call.** A line is down and it might be the software. Somebody with your number
is standing next to it. What you want to know before you accept the job: is there a formal on-call
rota, is it paid, how often does it actually fire, and can you connect remotely or is it a drive.
The answers vary enormously between companies and are almost never in the advert.

---

## 4. The Italian you will actually hear

Technical terms are largely borrowed from English, as [module 17](../module-17-career-emilia-romagna/)
says. The vocabulary that is *not* borrowed is exactly this domain's, and knowing twenty words
changes how the first day feels.

| Italian | What it is |
|---|---|
| **la commessa** | the job order — the unit everything is organised around |
| **il cantiere** | the customer's site during installation |
| **la trasferta** | travel to it, and the allowance paid for it |
| **il collaudo** | acceptance test — FAT or SAT |
| **la messa in servizio** · **l'avviamento** | commissioning · start-up |
| **il capitolato** | the customer's specification, which you are contractually held to |
| **il fermo macchina** | machine downtime |
| **la linea** | the production line |
| **la supervisione** | the SCADA / supervisory layer |
| **il quadro elettrico** | the electrical cabinet |
| **il PLC** | said as in English, and everywhere |
| **il nastro trasportatore** | the conveyor belt |
| **il trasloelevatore** | the stacker crane in an automated warehouse |
| **il magazzino automatico** | the automated warehouse itself |
| **l'UDC — unità di carico** | the load unit: whatever is being moved as one thing |
| **il bancale** · **il pallet** | pallet — `bancale` is the word people actually say |
| **il collo** | a package or parcel, as a countable item |
| **il prelievo** · **il picking** | picking |
| **il lotto** | the batch or lot, in the traceability sense |
| **la tracciabilità** | traceability |
| **la ricetta** | the recipe: the parameters for making one product |
| **il turno** | the shift |
| **il pezzo buono** · **lo scarto** | good piece · scrap |
| **il pulsante a fungo** | the emergency stop — the mushroom-headed red button |
| **la manutenzione** | maintenance (`preventiva`, `predittiva`) |
| **il muletto** · **il carrello elevatore** | forklift |
| **il magazziniere** | warehouse operator |
| **l'ordine di produzione** | the production order, from the ERP |

Two of these are worth memorising above the rest, because they appear in adverts and you will be
asked about them directly: **`cantiere`** and **`trasferta`**.

---

## 5. Where you fit, and the sentence that makes it credible

The most common self-inflicted wound in this market is overclaiming. Automation companies employ
people who have programmed PLCs for fifteen years, they can tell in one question, and being caught
costs you the rest of the interview.

**Say where your boundary is.** Something like:

> *"I am a .NET developer, not a PLC programmer, and I would not claim otherwise. I work above the
> PLC — I take the tags, keep the record, dispatch the work and answer to the ERP. To understand
> what I am being handed I wrote a Modbus TCP client and server by hand and an OPC UA client, so I
> know what a register map costs and what an information model buys."*

That answer is respected, for three reasons: it is honest, it is specific, and it describes exactly
the person they are hiring. They are not looking for a second electrical engineer. They are looking
for somebody who can write maintainable C# **and** talk to the electrical engineer without either
of them getting frustrated.

And in Italian, when you can:

> *"Ho lavorato sulla supervisione: ordini di trasporto, tracciabilità dei lotti e gestione del
> traffico. So che gran parte del lavoro vero è il collaudo e la messa in servizio in cantiere."*

The second sentence is the one that lands, because it says you know what you are signing up for.

---

## 6. The three questions to ask them

Ask these in the first interview. They are not aggressive — they are what an experienced person
asks, and asking them is itself a signal.

**1. "How much trasferta, realistically, in weeks per year?"**
The honest answer varies from four days to twenty weeks. Ask for the number, not the adjective.
Follow up with: is it Italy or abroad, how long is a typical trip, and how are weekends handled.

**2. "Who owns the PLC side, and how do we work together?"**
This tells you whether there is a real automation team or whether "the software guy" is expected to
absorb everything. It also tells you what the debugging loop looks like: if the PLC people are in
the next room, a mystery takes an hour; if they are a subcontractor in another country, it takes a
week.

**3. "What happens at 03:00 when a line is down?"**
Is there a rota, is it paid, how often does it fire, can you connect remotely. A company with a
good answer has thought about its people; a company that has never considered the question is
telling you something.

Two more worth having ready if the conversation allows: **"Is there already a historian, and what
do the machines speak?"** — which is a technical question that demonstrates you know the shape of
the work — and **"What proportion of the code is new development versus maintaining existing
commesse?"**, because the honest answer is often mostly the latter and you should want to know.

---

## Golden rules

1. **`Cantiere` and `trasferta` are the two words that decide your life**, and they are in the
   advert. Ask for weeks per year, not an adjective.
2. **Commissioning is the job, not the end of it.** Half the work happens on site, with the line
   stopped and people waiting.
3. **FAT is at your factory, SAT is at theirs**, and the payment milestone is usually attached to
   the second one.
4. **Everything is organised by `commessa`.** It is why this feels like project work even when the
   product is the same each time.
5. **You will not be there in four years.** Write the code and the comments for whoever answers the
   phone instead of you.
6. **On site, the software is the default suspect.** Being able to disprove it calmly, with the
   electrician rather than against them, is a large part of the actual skill.
7. **Never claim PLC experience you do not have.** Stating your boundary is what makes the rest of
   your claims credible.
8. **Ask about on-call before you accept**, not after — rota, pay, frequency, and whether you can
   connect remotely.

---

[← OT security](06-ot-security.md) · [Module 28](README.md)
