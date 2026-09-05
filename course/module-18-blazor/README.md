# Module 18 — Blazor: a UI over the API

> C# in the browser, and the fourth layer of module 16 finally getting a face.
> This module builds on 📂 [`src/LogiFlow.Web`](../../src/LogiFlow.Web/) — a Blazor Web App that
> consumes the LogiFlow API over HTTP.

**Prerequisite:** modules 15 (ASP.NET Core) and 16 (the layer map).

**Why this module exists for you specifically:** Blazor is how a company with fifteen years of
WinForms and WPF, a large SQL Server estate and no JavaScript team builds web UI without hiring a
front-end department. That describes a great many manufacturing software houses in Emilia-Romagna,
and it is why "some Blazor" appears in so many of their adverts. See module 17.

---

## 1. Four render modes, one decision

| Mode | Runs where | First paint | Interactivity | Cost |
|---|---|---|---|---|
| **Static SSR** | Server, once | Fastest | **None** — it is HTML | Nothing |
| **InteractiveServer** | Server, over a SignalR circuit | Fast | Full | A stateful circuit **per user**, and a round trip per click |
| **InteractiveWebAssembly** | Browser, on the .NET WASM runtime | Slowest — the runtime must download | Full | A multi-MB first load; no server state |
| **InteractiveAuto** | WASM if cached, else Server | Fast | Full | Both models, and both sets of bugs |

📂 [`Components/App.razor`](../../src/LogiFlow.Web/Components/App.razor) picks
**InteractiveServer**, and the reasoning is written into
📂 [`LogiFlow.Web.csproj`](../../src/LogiFlow.Web/LogiFlow.Web.csproj):

- **The JWT never reaches the browser.** It lives in server memory; the browser holds only a
  circuit. That deletes the entire "where do I store the token" problem a WASM app must solve —
  the same argument as a backend-for-frontend in a JavaScript stack.
- **No runtime download**, which matters on a factory-floor tablet over site Wi-Fi.
- **The honest cost:** one stateful circuit per user, and every click is a network round trip.
  Cheap for tens of concurrent users on a LAN — which is what an internal *gestionale* front end
  is. Wrong for a public site with thousands, or for users on a satellite link.

**The question you will be asked:** *"Server or WebAssembly?"* The answer is a trade, not a
preference: latency and scale against payload size and offline capability. Say which you would pick
**and what would change your mind**.

---

## 2. The circuit, and why an error boundary is not optional

In InteractiveServer, the browser holds a WebSocket to the server. UI events go up; DOM diffs come
back. The component's state — every field in your `@code` block — lives **on the server**.

```
   BROWSER                                            SERVER
   ┌──────────────────┐    WebSocket (SignalR)   ┌──────────────────────────────────┐
   │  the DOM         │◄────── DOM diffs ────────│  YOUR COMPONENT STATE            │
   │  and nothing     │                          │  every field in every @code      │
   │  else            │─────── UI events ───────►│  block, per connected user       │
   └──────────────────┘                          │  the JWT lives HERE              │
     holds a circuit,                            └──────────────────────────────────┘
     not a token                                   a 10,000-row list is 10,000 rows
                                                   of server memory, per user
   an unhandled exception kills the CIRCUIT, not the component:
   the page greys out and the user loses what they were typing ⇒ ErrorBoundary
```

Two consequences people discover the hard way:

**An unhandled exception kills the circuit.** Not the component. The circuit. The whole page greys
out and the user loses what they were typing. That is why
📂 [`MainLayout.razor`](../../src/LogiFlow.Web/Components/Layout/MainLayout.razor) wraps `@Body` in
an `ErrorBoundary`, and why 📂 [`LogiFlowApiClient`](../../src/LogiFlow.Web/Services/LogiFlowApiClient.cs)
returns `ApiResult` instead of throwing on a 404 or a 409.

**`ErrorBoundary` latches.** Once it catches, it keeps showing `ErrorContent` until you call
`Recover()`. Forgetting that is why "the error message never goes away".

```csharp
private void Recover() => _errorBoundary?.Recover();
```

**Circuit state is per-user memory on your server.** A component holding a 10,000-row list holds it
server-side, for every connected user, until they disconnect. Paging is not a nicety here; it is a
capacity decision.

---

## 3. The lifecycle bugs that cost everyone an afternoon

### `OnInitializedAsync` runs twice

Watch the log while loading an order detail page:

```
GET /api/orders/6581d1a6…   200
GET /api/orders/6581d1a6…   200      ← the same request, immediately
```

That is not a bug in your code. The component renders **twice**: once during prerendering (static
HTML, so the page is useful before the circuit connects) and again once the circuit is live. Both
passes run initialisation, so both fetch.

Your options, in order of preference:

1. **Accept it** for cheap, idempotent GETs — which is what this app does.
2. **Use `PersistentComponentState`** to hand the prerendered data to the interactive pass.
3. **Disable prerendering** — `@rendermode="new InteractiveServerRenderMode(prerender: false)"` —
   and accept a slower first paint.

**Never do side effects in `OnInitializedAsync` without thinking about this.** A POST there fires
twice.

### `OnInitializedAsync` does not re-run on navigation

📂 [`OrderDetailPage.razor`](../../src/LogiFlow.Web/Components/Pages/OrderDetailPage.razor) uses
`OnParametersSetAsync`, with the reason in a comment:

> Blazor **reuses the component instance** when navigating between two URLs matching the same
> `@page` route. Navigate from `/orders/A` to `/orders/B` and initialisation does not run again —
> so order A stays on screen forever.

This is the single most common Blazor routing bug. `OnParametersSetAsync` runs on every parameter
change, which is what "the route changed" actually is.

---

```
   FIRST LOAD of /orders/A
   ─────────────────────────────────────────────────────────────────────────────
   prerender pass    OnInitializedAsync  ✓   → static HTML, useful before the circuit
   circuit connects
   interactive pass  OnInitializedAsync  ✓   → the SAME fetch, again.  Two GETs.

   NAVIGATE /orders/A ──► /orders/B   (same @page route ⇒ the instance is REUSED)
   ─────────────────────────────────────────────────────────────────────────────
                     OnInitializedAsync  ✗   never runs again — order A stays on screen
                     OnParametersSetAsync ✓  this is what "the route changed" actually is
```

## 4. Talking to the API

📂 [`Services/LogiFlowApiClient.cs`](../../src/LogiFlow.Web/Services/LogiFlowApiClient.cs) ·
📂 [`Services/AccessToken.cs`](../../src/LogiFlow.Web/Services/AccessToken.cs)

**A typed client, not `HttpClient` injected into components.** One place holds the base address,
the auth handler, the JSON options and the error translation. A component reaching for `HttpClient`
directly bypasses all four.

**A `DelegatingHandler` attaches the token.** It is the `HttpClient` pipeline's middleware, and it
composes the same way (module 15). It runs for every request through the client, including ones
added next year by someone who never read the file.

**The token client is a *different* client, deliberately.** Minting a token through the
authenticated client would require a token in order to fetch the token — infinite recursion.

**Every method returns a result.** A 404 from a typed URL and a 409 from a stale tab are ordinary
outcomes, not exceptions. Same argument as `Result<T>` in the domain (module 05), enforced by a
harsher runtime: here an exception costs the user their session.

### The contract is duplicated on purpose

📂 [`Contracts/OrderContracts.cs`](../../src/LogiFlow.Web/Contracts/OrderContracts.cs) redeclares
`OrderSummary`, `OrderDetail` and `OrderStatus` rather than referencing `LogiFlow.Application`.

> Share types across a boundary you own on both sides and deploy together. **Duplicate them across
> a boundary that is a published contract.** A rename in the Application layer *should* break the
> client — that is the contract doing its job. If the UI simply *is* the server's types, there is
> no contract, only coupling.

Note the enum values are written out explicitly. The API registers no `JsonStringEnumConverter`, so
`OrderStatus` travels as an **integer**. Get those values out of order and shipped orders quietly
display as drafts.

---

## 5. Let the server own the state machine

The best thing in this UI is a piece of code that is not there.

`OrderDetailDto` carries `AllowedTransitions`, computed by `OrderStateMachine` in the domain. The
detail page renders buttons from that list:

```razor
@if (CanTransitionTo(OrderStatus.Submitted)) { <button …>Submit order</button> }
@if (CanTransitionTo(OrderStatus.Cancelled)) { <button …>Cancel order</button> }
```

**There is not one `if (status == Draft)` in the whole page.** Add a state to the domain and the UI
follows; reimplement the transition table in the client and the two drift apart the first time
somebody changes a rule. This is an underused API design technique and it is worth naming in an
interview.

You can watch it work: submit a Draft order and the "Submit" button disappears by itself, replaced
by a note that Confirmed is driven by the warehouse endpoints.

---

## 6. Concurrency, from the UI's side

Module 07 taught optimistic concurrency from the database's side. Here is what it looks like at the
top of the stack.

Open an order in two tabs. Submit in the first. Now submit in the second:

```
POST /api/orders/{id}/submit   409
GET  /api/orders/{id}          200      ← the page reloads itself
```

📂 `RunAsync` in `OrderDetailPage.razor` reloads after a 409 **and keeps the error visible**. The
user sees both what went wrong and the current truth, and the buttons re-derive themselves. A UI
that only shows the error leaves the user staring at a screen that disagrees with the database.

The same method reloads after every successful command rather than patching state locally. One
extra GET beats guessing which of status, transitions, timestamps and reserved stock changed.

---

## 7. What the user sees while it is loading

A list has three states, not two, and most UIs only build two of them:

| State | What is on screen | What this app draws |
|---|---|---|
| **Nothing yet** | No data has ever arrived | Shimmering **skeleton rows** — the shape of the answer before the answer |
| **Stale but useful** | Last page still valid, new one in flight | The old rows, **dimmed**, plus a 2px sweep at the table's top edge |
| **Fresh** | Data is current | The rows, and new ones slide in |

📂 [`Orders.razor`](../../src/LogiFlow.Web/Components/Pages/Orders.razor) picks between them on
`_loading && _page is null`. The distinction matters: swapping real rows for grey bars on every
keystroke is flicker, not feedback, and the user is usually comparing the new list against the one
they can no longer see.

The old version covered the table with a `Loading…` veil. A veil hides the data you are waiting to
compare against, and it lands in the middle of the table where nothing else ever appears. A sweep
at the edge says the same thing and takes nothing away.

### `@key`, and why an animation is a diffing test

```razor
<tr class="row-link" @key="order.Id" @onclick="() => OpenOrder(order.Id)">
```

Without `@key`, Blazor matches old and new rows **by position**. Page 1 to page 2 patches the text
inside ten existing `<tr>` elements — no element is created, so nothing is "new". With `@key`, it
matches by identity: genuinely new rows are new elements.

That has a correctness half and a visible half, and the visible half is how you test the other one:

- **Correctness.** Put an `<input>` in a row without a key, sort the table, and the text follows the
  *position*, not the order. The classic version of this bug is a checked checkbox that ends up on
  the wrong record.
- **Visible.** CSS entrance animations run when an element is **inserted**, never when it is
  re-rendered. So "which rows slid in?" is a live readout of what the diff decided. Re-sorting the
  same ten orders moves them without a flash; paging replaces all ten and the whole page cascades.

### Debounce without a `Timer`

The search box fires on `@oninput` — one request per keystroke would be absurd, so it waits:

```csharp
private async Task OnSearchInput(ChangeEventArgs e)
{
    _request.SearchTerm = e.Value?.ToString();

    if (_debounce is not null)
    {
        await _debounce.CancelAsync();
        _debounce.Dispose();
    }

    _debounce = new CancellationTokenSource();

    try { await Task.Delay(SearchDebounce, _debounce.Token); }
    catch (OperationCanceledException) { return; }   // a later keystroke owns the search now

    await ResetAndLoadAsync();
}
```

**Why an awaited delay rather than `System.Timers.Timer`.** Blazor's renderer has its own
`SynchronizationContext`. This `await` started inside an event handler, so the continuation resumes
**on that context** and touching component state afterwards is safe. A timer's `Elapsed` fires on a
thread pool thread, where the same code is a race — that is the whole reason
`InvokeAsync(StateHasChanged)` exists, and forgetting it produces the maddening
"everything updated except that one component".

**Two token sources, not one.** `_debounce` abandons a search that has not started; `_cts` abandons
one already in flight (module 15's cancellation story, seen from the client). Share them and the
first keystroke after a slow request cancels the request it was waiting for.

The one place this app *does* start work nobody awaits is the toast in
📂 [`OrderDetailPage.razor`](../../src/LogiFlow.Web/Components/Pages/OrderDetailPage.razor):

```csharp
_ = DismissAfterDelayAsync(_noticeCts.Token);   // awaiting would hold the click open for 5s
```

The discard says it is deliberate; the token is what stops it being a leak; `Dispose` cancels it, so
navigating away does not leave a timer holding a disposed component. That trio — discard, token,
dispose — is what separates fire-and-forget from a bug.

### Two more that only bite in production

**Culture, again — this time in CSS.** The lifecycle rail's fill and the mix bar's widths are
written as inline styles from C#:

```csharp
string.Create(CultureInfo.InvariantCulture, $"width:{percent:0.##}%");
```

On an Italian machine the default formatting yields `width:33,33%`, which is not a CSS length. The
browser drops the declaration silently, the segments collapse, and nothing in any log mentions
culture. Same lesson as every `ToString` in this repo (module 00), arriving through the stylesheet.

**`prefers-reduced-motion`.** Four lines at the bottom of
📂 [`app.css`](../../src/LogiFlow.Web/wwwroot/app.css), and note *what* they switch off:

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation-duration: 1ms !important; transition-duration: 1ms !important; }
}
```

Durations, not the rules. An element with `animation: rise both` must still land on its final frame
— remove the animation outright and it stays invisible. Vestibular disorders are not an edge case,
and this is cheaper than the meeting about it.

---

## 8. Cascading values, and the parameter trap

The **Explain** switch in the topbar turns on plain-language notes across every page — off by
default, so a colleague sees a normal admin tool. It is also the smallest interesting example of
sharing state between components.

📂 [`MainLayout.razor`](../../src/LogiFlow.Web/Components/Layout/MainLayout.razor) holds the state
and cascades it; 📂 [`Explain.razor`](../../src/LogiFlow.Web/Components/Shared/Explain.razor) is the
only thing that reads it:

```razor
<CascadingValue Value="_explain">
    @Body
</CascadingValue>
```

**Why not a scoped service with an event?** It works, and every subscriber must then remember to
unsubscribe in `Dispose`. One page that forgets leaks for the lifetime of the circuit — and in
Blazor Server a circuit can live for hours. A cascading value has no subscription to leak.

**Why a type, not `[CascadingParameter(Name = "ExplainMode")]`.** A magic string typo compiles
happily and then silently never updates. 📂 [`ExplainMode`](../../src/LogiFlow.Web/Components/Shared/ExplainMode.cs)
is a record, and a type cannot be misspelled.

**Not `IsFixed`.** Fixed cascades are the cheaper ones and the right default for values that never
change — but a fixed toggle is a toggle that does nothing.

### The trap

**A cascading parameter change re-runs `OnParametersSetAsync` on the component that declares it.**

Look back at section 3: `OrderDetailPage` loads the order in `OnParametersSetAsync`, because that is
the only hook that runs when the route changes. So this innocent line —

```csharp
[CascadingParameter] public ExplainMode? Mode { get; set; }   // do not add this to that page
```

— turns every click of the Explain switch into a `GET /api/orders/{id}`. Nothing warns you; the page
just quietly becomes chatty.

That is why the pages declare nothing and the leaf component declares everything. `<Explain>` renders
nothing at all when the mode is off, so a page can be written with these sprinkled through it and
still cost nothing when the switch is off.

**Where the state lives.** In the layout, which survives client-side navigation — open an order and
come back and the switch is still on. A full page reload resets it, and persisting it would mean JS
interop, which cannot run during prerendering; the resulting flash of the wrong state costs more
than it buys.

### Why bother at all

Two plain sentences and a **Grown-up words** line under each: the filing cabinet you never carry
across the room (server-side paging), the librarian instead of reading every book (a `WHERE` clause
in SQL), the board-game piece that cannot hop backwards (the domain's state machine), two
calculators that always end up disagreeing (why the client never recomputes a total).

That is not decoration for the user. **It is rehearsal for you.** "We page in the database" is a
claim anyone can memorise; being able to say *why* in one sentence with no jargon is what an
interviewer is actually testing, and module 17 says why that matters in this market.

---

## 9. Small things that are easy to get wrong

| | |
|---|---|
| **`app.UseAntiforgery()`** | Required, and must precede `MapRazorComponents`. Omit it and form posts fail with an error that names none of this |
| **`blazor.web.js`** | Not `blazor.server.js`. The web variant is the unified script for the Web App model |
| **`AddInteractiveServerComponents()` *and* `AddInteractiveServerRenderMode()`** | Services and endpoints. With only one, components render once as static HTML and no button does anything |
| **`FocusOnNavigate`** | Two lines in `Routes.razor`, and the difference between a screen-reader user hearing the new page or hearing nothing |
| **Cancel superseded requests** | 📂 `Orders.razor` holds a `CancellationTokenSource` per search. Without it, a slow response for an abandoned query overwrites a fast one for the current query |
| **A real `<a href>` for row links** | Middle-click, ctrl-click and "copy link address" must work. A `div` with an `@onclick` breaks all three |
| **`aria-sort` belongs on the `<th>`** | Not on the button inside it. 📂 `SortHeader.razor` renders the whole header cell so no call site can forget it |
| **`role="status"`, not `role="alert"`** | A success toast is announced after the screen reader's current sentence. `alert` interrupts, and "it worked" never earns an interruption |
| **Colour is never the only signal** | Every pill carries its text, and the pulsing dot marks *moving* states — a pill that means something only if you can tell teal from olive is unreadable to ~8% of men |

---

## Do it

**1. Run it.** Two terminals:

```bash
cd src/LogiFlow.Api && dotnet run
cd src/LogiFlow.Web && dotnet run
```

Then open <http://localhost:5280>.

**2. Watch the double render.** Load an order detail page and count the `GET /api/orders/{id}`
lines in the API's console. There are two. Now set
`@rendermode="new InteractiveServerRenderMode(prerender: false)"` on `<Routes>` and count again.

**3. Reproduce the routing bug.** Change `OnParametersSetAsync` to `OnInitializedAsync` in
`OrderDetailPage.razor`. Navigate from the list to order A, back, then to order B. **Order A is
still on screen.** Change it back.

**4. Kill a circuit.** Add `throw new InvalidOperationException("boom");` to a button handler and
click it, first with the `ErrorBoundary` in `MainLayout` and then with it removed. The difference
between a contained message and a dead grey page is the whole argument for the boundary.

**5. Build the create-order flow.** `LogiFlowApiClient` already has `CreateOrderAsync` and
`AddLineAsync`, and nothing calls them. Add a page that creates a draft and adds lines to it. You
will immediately hit the real question: the UI needs a customer list and a product list, and the
API has no endpoint for either. Deciding whether to add one, reuse `/api/reports/customers`, or
change the command, *is* the exercise.

**6. Add the idempotency filter from module 15**, then turn on
`.AddStandardResilienceHandler()` for the typed client. Read the comment in the `.csproj` first —
it explains why retries are currently switched off, and why that filter is the precondition.

**7. Delete `@key` from the row loop** in `Orders.razor`, then page back and forth. The rows stop
animating in, because none of them are new any more — Blazor is patching ten elements it kept. Now
add an `<input>` to each row, type in one, and re-sort: the text stays with the *position*. Put the
key back.

**8. Break the debounce on purpose.** Replace the awaited `Task.Delay` with a
`System.Timers.Timer` and call `StateHasChanged()` directly from `Elapsed` — no `InvokeAsync`. It
will work on your machine most of the time, which is the point: run it, then read the exception when
it does not.

**9. Spring the cascading-parameter trap.** Add
`[CascadingParameter] public ExplainMode? Mode { get; set; }` to `OrderDetailPage`, open an order,
and click the Explain switch four times while watching the API console. Four `GET /api/orders/{id}`.

**10. Turn on reduced motion** (Windows: Settings → Accessibility → Visual effects → Animation
effects off) and reload. Everything should arrive instantly and nothing should be invisible — an
element animated with `both` that never runs is an element that never appears.

---

## Golden rules

> The card.

1. **Choose the render mode on users, network and secrets** — not on preference. Server: fast
   first paint, no download, the token stays server-side, one stateful circuit per user.
   WebAssembly: heavy first load, no server state, scales like static files.
2. **In Blazor Server, component state is server memory.** Paging a large list is a capacity
   decision, not a nicety.
3. **`ErrorBoundary` is not optional, and it latches.** Until you call `Recover()`, the error
   message never goes away.
4. **Do not throw for expected failures.** A 404 or a 409 is an ordinary outcome; here an
   exception costs the user their session.
5. **A typed client, and a `DelegatingHandler` for the token.** One place owns the base address,
   the auth header, the JSON options and the error translation — including for the code someone
   adds next year.
6. **Duplicate the contract across a published boundary.** A rename in the Application layer
   *should* break the client; if the UI simply is the server's types, there is no contract, only
   coupling. And write enum values out explicitly when they travel as integers.
7. **Let the server own the state machine.** Render the buttons from `AllowedTransitions`; the
   moment the UI reimplements the transition table, the two start drifting.
8. **`OnInitializedAsync` runs twice on first load and never again on a route change.** Prerender
   plus interactive is two fetches; navigation between two URLs on the same `@page` reuses the
   instance. Load data in `OnParametersSetAsync`.
9. **`@key` every repeated element.** Without it the diff matches by position, and state inside a
   row — input text, focus, a checked box — follows the position instead of the record.
10. **Debounce with an awaited `Task.Delay` and a token, not a `Timer`.** The continuation resumes
    on the renderer's synchronisation context; a timer callback does not, and that is where the
    race lives. Anything touching state from off that context needs
    `InvokeAsync(StateHasChanged)`.
11. **Two cancellation sources: one for the keystroke, one for the request in flight.** Share them
    and the next keystroke cancels the request it was waiting for.
12. **Fire-and-forget needs the trio: a discard, a token, and a `Dispose` that cancels it.**
    Otherwise navigating away leaves a timer holding a disposed component.
13. **Format anything that becomes CSS with `InvariantCulture`.** On an Italian machine
    `width:33,33%` is silently dropped by the browser and nothing anywhere mentions culture.
14. **Reload after a 409 and keep the error visible.** A UI showing only the error leaves the user
    staring at a screen that disagrees with the database.
15. **Respect `prefers-reduced-motion` by shortening durations, not removing animations** — an
    element animated with `both` that never runs is an element that never appears.

---

## Interview questions

**"Blazor Server or WebAssembly?"**
Server: fast first paint, no download, server-side secrets, but a stateful circuit per user and a
round trip per interaction. WebAssembly: heavy first load and no server state, but it scales like
static files and works offline. Pick on user count, network, and whether secrets must stay server-side.

**"What happens when a Blazor Server user loses connection?"**
The circuit is retained briefly and can reconnect with state intact; beyond that window it is
dropped and the user gets a reload. Which is why long forms should not hold all state in circuit
memory.

**"Why is my page loading data twice?"**
Prerendering. The component renders statically, then again interactively, and both passes run
initialisation. Fix with `PersistentComponentState` or by disabling prerender.

**"I navigated to another order and the page did not update."**
`OnInitializedAsync` does not re-run when the component instance is reused across a route change.
Use `OnParametersSetAsync`.

**"Where do you put the access token in a Blazor Server app?"**
On the server. The browser holds a circuit, not a token — one of the model's real advantages.

**"How do you stop one component's exception taking down the page?"**
`ErrorBoundary`, remembering it latches until `Recover()`. And do not throw for expected failures
in the first place — return a result.

**"How would you stop a search box firing a request per keystroke?"**
Debounce it, and cancel the in-flight request when a newer one starts. In Blazor, an awaited
`Task.Delay` against a `CancellationTokenSource` is enough — the continuation resumes on the
renderer's synchronisation context, so no `InvokeAsync` is needed. A timer callback does not, and
that is where the race lives.

**"My UI updated everywhere except one component."**
Something called `StateHasChanged` off Blazor's synchronisation context — a timer, a background
task, an event from a singleton service. Marshal back with `InvokeAsync(StateHasChanged)`.

**"Cascading value or a scoped service for shared UI state?"**
A cascading value for something the render tree consumes: no subscription, so nothing to leak. A
service with an event when non-components need it too — and then every subscriber owns an
unsubscribe in `Dispose`. Watch out for cascading parameters on a component that loads data in
`OnParametersSetAsync`; the change re-runs it.

**"What does `@key` actually do?"**
Tells the diff to match elements by identity instead of position. Without it, state inside a
repeated element — input text, focus, a checkbox — follows the row's position when the collection
reorders.

---

## You have finished the course

Go back to [`course/README.md`](../README.md) and check what you skipped. Keep
[`GOLDEN-RULES.md`](../GOLDEN-RULES.md) — every module's rules on one page — for the week before
an interview.

Then do the thing that actually consolidates it: **add a feature end to end.** Returns and refunds
is a good one — a new aggregate, a state machine, domain events, a read model, endpoints, a page in
the UI, and tests at all three levels. If you can do that without referring back, you know this
material.

---

## Next

→ [Module 19 — Memory, the heap, and the garbage collector](../module-19-memory-and-gc/)

That is the whole application, top to bottom. **Part V now goes underneath it**: the runtime your
C# has been running on all along. Modules 19 to 22 are the ones most .NET developers never learn,
and they are what an interviewer is probing for when the questions stop being about syntax.

**Back to:** [the course index](../README.md) · [module 17 — the job](../module-17-career-emilia-romagna/)
