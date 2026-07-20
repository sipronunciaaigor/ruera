# Ruera — Documento di design

> Stato: bozza consolidata delle decisioni di design. Nessun codice ancora scritto.
> Ultimo aggiornamento: 2026-07-17

## 1. Visione

Gestionale/simulazione storica sulla raccolta e il ciclo dei rifiuti, dal 1880 al 2050 e oltre.

**Tesi del gioco:** il circolo virtuoso dei rifiuti conviene — la spazzatura è materia prima, e chi chiude il ciclo guadagna.

Il giocatore parte con un capannone di lamiere in periferia e qualche soldo, nell'epoca di ruée e navazzari privati, e attraversa 170 anni di storia della nettezza urbana: gerle, bidoni, sacchi, differenziata, ricette chimiche, fino al recupero orbitale e delle scorie.

I titoli degli scenari usano i nomi dialettali della spazzatura: **Rusco** (Bologna), **Ruera** (Milano), **Rumenta** (Genova).

**Roadmap di prodotto:** `Ruera` (V1) è il gioco completo ma snello. Se ha successo, `Ruera 2` sarà una **riscrittura da zero** (alla SimCity → SimCity 2000), con team e budget. Le scelte di V1 non devono ipotecare V2: possono permettersi di essere semplici.

Esiste un possibile business secondario: una versione iperrealistica da vendere a città/municipalizzate per forecasting. **È secondario e dipende dal successo del gioco**; l'unico requisito che impone oggi è tenere il motore deterministico e guidato da dati (parametri esterni al codice), così la calibrazione futura resta possibile.

---

## 2. Architettura di simulazione

### Motore unico a eventi discreti

- **Un solo motore** per tutte le modalità e tutte le viste. Timing, economia e tutto il resto sono dati dai tick.
- **Tick = 1 giorno di gioco.** La durata reale di un tick (1 secondo o 100) è solo velocità di rendering.
- **Deterministico dal giorno zero**: stesso seed + stessi input = stessa partita, identica al bit. Niente casualità non seedata, niente dipendenze dal frame rate. Abilita: replay, ghost PvP, esecuzione headless, test automatici di bilanciamento (simulare 50 anni in secondi).
  - Strategia decisa (RUE-7): unità intere a 64 bit — vedi «Determinismo: strategia» sotto.
- **Viaggi parametrici, niente fisica**: tempo totale di un mezzo = f(distanza dalla centrale, riempimento, tempo di svuotamento dei punti). Il traffico in grafica è verosimile, non vero.

### Determinismo: strategia *(deciso 2026-07-17 — RUE-7)*

**Decisione: unità intere a 64 bit in tutta la simulazione; la virgola mobile non esiste in `Ruera.Sim`.**

La sim di Ruera è logistica discreta + contabilità: minuti, metri, grammi, centesimi. Non servono funzioni trascendenti né fisica continua, quindi non serve il floating point — che in .NET non garantisce risultati bit-identici tra piattaforme/JIT per le funzioni di libreria (`Math.Sin/Exp/Pow` variano per OS e architettura) e nasconde trappole (SIMD, ordine di somma). Gli interi sono deterministici per costruzione, su qualunque macchina. Il fixed-point custom (es. Q32.32) resta l'opzione di riserva se un giorno servissero curve continue nella sim: introducibile dopo come struct dedicata, senza rompere l'impianto.

**Regole di codifica** (vincolanti per `Ruera.Sim`):

1. Tutte le grandezze di stato sono `long` incapsulati in `readonly record struct` di unità (`Minutes`, `Meters`, `Grams`, `Cents`, …). Vietati `float`, `double`, `decimal` in stato e logica.
2. Le divisioni dichiarano l'arrotondamento: helper `MulDiv` (intermedio `Int128`, niente overflow), `DivFloor`, `DivRound`. Percentuali e tassi in basis points (1/10 000) applicati via `MulDiv`.
3. RNG: implementazione propria committata in repo (xoshiro256\*\*). Mai `System.Random` (la sequenza seedata non è garantita stabile tra versioni .NET) né `Guid.NewGuid()`. Stream per sistema derivati dal seed master (SplitMix64 su id di sistema): aggiungere una chiamata random in un sistema non sposta le sequenze degli altri.
4. Il tempo è il contatore di tick: vietati `DateTime.Now/UtcNow`, `Stopwatch`, `Environment.TickCount` nella sim.
5. Mai iterare `Dictionary`/`HashSet` nella logica: entità con ID densi, iterazione in ordine di ID o su strutture ordinate esplicitamente.
6. Niente parallelismo nel calcolo dello stato (`Parallel`, PLINQ, `Task`): la sim è single-thread dentro il tick. Il rendering fa ciò che vuole.
7. Mai `GetHashCode()` nella logica o nell'hash di stato (quello delle stringhe è randomizzato per processo): hash di stato via FNV-1a/xxHash64 implementati in repo su serializzazione canonica.
8. Parsing/formatting dei file dati con `InvariantCulture`; i file dati esprimono le quantità già in unità intere.
9. I `float` vivono solo nel layer di presentazione (Godot) per l'interpolazione visiva e non rientrano mai nella sim.

**Enforcement** (si monta con RUE-11): `Microsoft.CodeAnalysis.BannedApiAnalyzers` su `Ruera.Sim` (BannedSymbols: `System.Random`, `DateTime.Now`, …) + test architetturale via reflection che verifica l'assenza di campi float/double/decimal nei tipi di stato.

**Strategia di test anti-regressione**:

- *ripetizione*: due run con stesso seed e stessi comandi → hash identico a ogni tick;
- *golden hash*: scenari scriptati con hash attesi committati (si aggiornano solo consapevolmente);
- *cross-TFM*: `Ruera.Cli` multi-target net9.0/net10.0, output confrontati (in CI);
- *cross-OS*: stesso confronto su Windows/Linux quando esisterà una pipeline.

### La giornata come problema di capacità

Il giro del camion non è stato simulato nel tempo: è un **piano di fattibilità calcolato dentro il tick**.

- Ogni squadra ha un budget di minuti (es. turno di raccolta 2–10 = 480 minuti).
- I tempi parametrici di viaggio e svuotamento consumano il budget.
- Domanda > capacità → punti non serviti → accumulo → violazioni sanitarie, multe, appalti a rischio.
- Turni indicativi: raccolta esterna 2–10, cernita in azienda 10–18, vendita 8–20, R&D. I macchinari a ciclo continuo (inceneritore, tritarifiuti, compattatori) lavorano fuori turno.

La grafica è la **messa in scena** del piano calcolato, non la simulazione.

### Risoluzione al tick e cadenze economiche *(deciso 2026-07-17 — RUE-6)*

**Decisione: ogni effetto si materializza al confine del tick. Nella simulazione non esistono checkpoint sub-tick; l'aggregazione (Arcade, §6) avviene sopra i risultati per-tick, mai al posto loro.**

Il dubbio (§15.8) era se alcuni effetti dovessero materializzarsi ai checkpoint sulla mappa (es. al rientro del mezzo in azienda), aggregando più tick. Risposta: no, per costruzione —

- **Nel motore non esiste tempo sub-tick.** La giornata è un piano di fattibilità calcolato dentro il tick (sopra): «il camion è rientrato» non è un evento da aspettare, è un fatto già contenuto nel piano che il tick stesso ha calcolato — i suoi effetti si scrivono alla chiusura di quel tick. Un checkpoint introdurrebbe una seconda nozione di tempo, con un ordinamento di eventi sub-tick da rendere a sua volta deterministico: complessità pura, zero profondità decisionale.
- **Replay e comandi restano banali.** I comandi del giocatore si applicano all'apertura del tick, il log dei comandi è indicizzato per tick, l'hash di stato avanza una volta per tick: la bisezione di una divergenza è per-tick (strategia di test sopra).
- **Il realismo lo danno le cadenze, non i checkpoint**: anche nella realtà i flussi di cassa seguono il calendario (la paga del sabato, il canone mensile), non il rientro fisico del mezzo. Il calendario sopra i tick esiste già (RUE-11).

I «checkpoint» sopravvivono in due forme, nessuna delle due nella sim:

- **Messa in scena** (grafica): nella riproduzione del tick la UI può inscenare l'effetto nel momento verosimile — l'inventario cresce a schermo quando il camion arriva in azienda — ma il libro mastro è cambiato una volta sola, a chiusura tick.
- **Effetti a tick futuro**: i processi multi-giorno (consegne, training, scadenze normative) sono effetti programmati su tick futuri — *il checkpoint è un tick*, non un luogo sulla mappa. È il pattern già in uso per i «Ritardi realistici» (sotto).

**Ordine fisso dei sistemi dentro il tick** (vincolante per RUE-16 e RUE-14):

1. applicazione dei comandi accodati per il tick;
2. calendario ed eventi (festività, scioperi, scadenze normative);
3. produzione rifiuti;
4. piano del giorno: solve ed esecuzione (raccolta, accumulo, violazioni);
5. lavorazione in azienda (cernita, macchinari a ciclo continuo);
6. vendite;
7. contabilità di chiusura (cadenze sotto) e hash di stato.

**Cadenze economiche** (sul calendario: settimana lavorativa di 6 giorni, domenica riposo):

| Flusso | Cadenza | Momento |
|---|---|---|
| Salari | settimanale | a chiusura del sabato; maturano per giorno lavorato |
| Appalti condominiali | mensile | primo tick del mese, canone del mese precedente |
| Vendita materiali | immediata | al tick della vendita |
| Manutenzione ordinaria | giornaliera | rata per tick per mezzo/macchinario posseduto |
| Riparazioni straordinarie | immediata | al tick del guasto |
| Multe | immediata | al tick dell'accertamento (ispezione o scadenza norma) |
| Acquisti | esborso subito | pagamento al tick dell'ordine; consegna a tick futuro programmato |
| Assunzioni | settimanale | in paga dal primo sabato; produttivi dopo ~10 tick di training |

Le cadenze sono **dati di scenario**, non costanti nel codice (§15.9): nel corso del '900 la paga passa da settimanale a quattordicinale/mensile — è progressione storica, non refactoring.

**Conseguenza per save/pausa/replay** (RUE-8, RUE-18): lo stato della sim esiste solo ai confini di tick; la pausa è un fatto di rendering; il salvataggio cattura l'ultimo tick concluso (più, al massimo, la posizione di riproduzione grafica).

### Ritardi realistici

- Gli acquisti (mezzi, macchinari) hanno tempi di consegna: non arrivano subito.
- Le assunzioni richiedono training: ~10 tick in cui la persona è solo un costo.
- Scopo: premiare la pianificazione, impedire il gioco puramente reattivo e l'assumi-e-licenzia.

### Requisiti trasversali

- **Salvataggio, pausa e replay integrale**: ogni partita deve poter essere salvata, messa in pausa e ri-eseguita. Gli input del giocatore sono un flusso di comandi serializzabili: partita = stato iniziale + seed + comandi. I replay servono anche a istruire le AI in futuro (Ruera 2) e a migliorare le simulazioni B2B.
- **Dati, non codice**: tipi di veicolo (nomi, capacità, tempi di riempimento/svuotamento, costi, epoche di disponibilità), tipologie di rifiuto e archetipi di produttore sono definiti in file di configurazione esterni — aggiungere un tipo non richiede ricompilare.
- **Ispezionabilità**: ogni entità (in primis i veicoli) è cliccabile e mostra il proprio stato corrente: carico vs capacità, rotta assegnata, budget consumato/residuo, avanzamento del piano del giorno.

### Save e replay: formato *(deciso 2026-07-17 — RUE-8)*

**Decisione: ibrido con gerarchia netta — il log dei comandi è la verità, gli snapshot sono cache di accelerazione.** Un salvataggio resta valido anche senza snapshot; gli snapshot si possono scartare e ricostruire in ogni momento.

- **Verità**: seed + identità scenario + log comandi (partita = stato iniziale + input). Il log usa la codifica binaria little-endian versionata introdotta con RUE-15.
- **Snapshot**: serializzazione canonica dello stato a fine tick — uno ogni 365 tick (un anno di gioco) più uno al momento del salvataggio. Caricare = snapshot più vicino + ri-simulazione della coda di tick. Gli snapshot servono anche allo scrubbing dei replay e da auto-verifica: ognuno registra l'hash di stato e la ri-simulazione deve atterrarci sopra — il non-determinismo emerge da solo sul campo.
- **Regola del writer unico**: hash di stato e snapshot condividono la stessa serializzazione canonica (un solo writer alimenta sia FNV-1a sia i byte dello snapshot) — mai due ordinamenti di campi da tenere allineati a mano. Oggi `SimState.AddToHash` alimenta solo l'hash: il writer si estrae con RUE-18, senza duplicare l'ordine dei campi.

**Contenitore** (binario, un solo file; checksum FNV-1a per sezione, deflate opzionale per sezione):

| Sezione | Contenuto |
|---|---|
| Header | magic `RUERA`, versione contenitore, `SimVersion`, `StateSchemaVersion`, id scenario + hash dei dati di scenario, seed, tick salvato, hash di stato |
| Log comandi | codifica RUE-15 (con la propria versione wire) |
| Indice snapshot | (tick, offset, hash) per ogni snapshot |
| Snapshot × N | stato canonico a fine tick |

Replay e ghost usano lo stesso contenitore (snapshot facoltativi, utili per lo scrubbing).

**Versioning**:

- `SimVersion` (intero monotono): si incrementa a **ogni modifica che cambia la traiettoria dell'hash** a parità di seed+comandi — cioè ogni volta che si aggiornano consapevolmente i golden hash. È l'etichetta di determinismo del motore.
- **Replay e ghost richiedono `SimVersion` identico** (§10: le patch rompono i replay — è previsto, i ghost sono etichettati).
- Salvataggi attraverso le patch: stesso `SimVersion` → tutto funziona (snapshot + coda). `SimVersion` diverso ma `StateSchemaVersion` uguale → si continua dall'ultimo snapshot; il replay pregresso resta visualizzabile solo sulla versione vecchia. `StateSchemaVersion` diverso → salvataggio non caricabile (accettato pre-1.0: niente migrazioni in V1).
- L'header registra anche l'**hash dei dati di scenario** (RUE-12/RUE-20): dati diversi = partita diversa, stessa logica del codice.

**Stima di ingombro** (partita di 50 anni ≈ 18 250 tick), assunzioni esplicite:

| Voce | Assunzione | Raw | Compresso (~×3) |
|---|---|---|---|
| Comandi semplici | ~5/tick × 16 B | ~1,5 MB | ~0,5 MB |
| Comandi di pittura | ~2/settimana × ~400 B | ~2 MB | ~0,7 MB |
| Snapshot annuali | 50 × 0,3–3 MB (cresce col gioco) | 15–150 MB | 5–30 MB |
| **Totale** | | | **≈ 6–31 MB** (tipico < 5 MB a metà partita) |

Numeri banali per il disco. Se gli snapshot pesassero, si dirada la ritenzione (annuali recenti + decennali) senza toccare il formato.

**Implicazioni**: RUE-18 implementa il contenitore e estrae il writer canonico unico; `Simulation.Replay` dovrà accettare l'identità di scenario oltre al seed (oggi assume il calendario di default — rilevante anche per RUE-20).

### Moddabilità: vincoli dal giorno uno *(deciso 2026-07-18)*

**Decisione: il modding di contenuti è un requisito architetturale di V1; il modding di codice è rimandato, ma i punti di innesto si proteggono da subito.** Alla Cities: Skylines, il modding è un motore di adozione — e le scelte che lo rendono possibile non si retrofittano.

Già mod-ready per costruzione (da proteggere, non da costruire):

- **Dati, non codice** (sopra): veicoli/rifiuti/archetipi/mappe/scenari sono file esterni — una mod di contenuto è «solo» un altro pacchetto di dati.
- **Formato mappa come contratto** (§11): qualunque tool di terzi può produrre mappe valide.
- **Hash dei dati di scenario nei salvataggi** (sopra): le partite moddate restano oneste su save/replay/ghost — contenuto diverso = partita diversa, già oggi.
- **Pipeline dei sistemi esplicita, id stabili append-only** (wire dei comandi, stream RNG): i punti di innesto per future mod di codice esistono già come seam interni.

Regole dal giorno uno:

1. **Id di contenuto namespaced**: ogni id è `pacchetto:nome` (`base:gerla`, `base:condo-small`). Evita le collisioni tra pacchetti; rinominare dopo romperebbe salvataggi e hash di scenario → si fa subito, finché il contenuto è placeholder.
2. **Il contenuto «base» è un pacchetto come gli altri**: il loader accetterà N pacchetti con ordine di carico e override espliciti (manifest, versioni, dipendenze: investigation dedicata; l'hash di scenario diventa hash dell'insieme dei pacchetti + ordine).
3. **Niente logica nei dati in V1**: i pacchetti dichiarano parametri, mai codice. Le mod di codice (sistemi custom, patching) sono materia post-V1/Ruera 2: dentro la sim richiederebbero le stesse regole di determinismo di §2, non esponibili in sicurezza oggi.
4. **UI e rendering moddabili lato Godot**: skin, icone, viste non toccano sim né determinismo; si valutano quando esiste la UI (RUE-17+).

### Scenario e timeline storica: dati, non codice *(deciso 2026-07-19 — RUE-20; implementato — RUE-38)*

**Decisione: lo scenario è l'unità di contenuto di primo livello — un pacchetto (§«Moddabilità») che raccoglie mappa, calendario, timeline storica e condizioni iniziali. La timeline è una lista di *effetti tipizzati e parametrizzati* su un vocabolario chiuso, non codice.** Chiude il debito §15.9: `SimCalendar.Milano1880()` diventa il caricamento del pacchetto `base:milano-1880`.

Il problema difficile è la **timeline storica di 170 anni in cui gli eventi cambiano le regole del gioco** (WWI: penuria di carburante, i cavalli tornano; autarchia anni '30: recupero materiali obbligatorio; norma inceneritori; la settimana lavorativa che nel '900 perde il sabato). Esprimere un «cambio di regola» come dato senza incorporare codice si risolve con lo **stesso principio dei comandi (RUE-15) e della moddabilità**: il motore possiede un insieme *chiuso* di applicatori di effetti; i dati **selezionano e parametrizzano**, non programmano.

**Due nozioni di «evento» che non vanno confuse** (entrambe girano nello step calendario/eventi del tick, RUE-6):

- **Eventi di timeline (scriptati, RUE-20)**: deterministici, parte dell'identità dello scenario (entrano nell'hash dei dati di scenario, RUE-8). Sono i binari storici: si applicano a un tick noto (o su condizione dichiarata). Nessun RNG.
- **Eventi stocastici (RUE-32)**: casuali, dallo stream RNG `Events` (guasti, ispezioni, bandi). L'`EventSettings` di RUE-32 **confluisce nel pacchetto scenario** come sua sezione.

**Formato** (JSON, `formatVersion`, unità intere, id namespaced §«Moddabilità») — *implementato in RUE-38* (`data/scenarios/<pkg>/scenario.json`):

```
scenario  = { formatVersion, id, name, map: <mapId>, calendar, timeline: [ entry… ], events?, end? }
calendar  = { epochYear, epochMonth, epochDay, restDays: ["sunday"…], holidays: [ {month,day,name}… ] }
entry     = { onYear, onMonth, onDay, effect }             // trigger = data civile; ordine dichiarato = ordine di applicazione
effect    = { type: <effectType>, …parametri }             // vocabolario chiuso
events?   = EventSettings di RUE-32 (assente = eventi off)  // gli stocastici confluiscono qui
end?      = { year, month, day }                           // fine opzionale: obiettivo §12, non vincolo del motore (vedi «Bounds»)
```

`onCondition` (trigger su predicato di stato invece che su data) è **riservato**: non è ancora un campo — si aggiunge quando un evento della slice lo richiede.

**Stato d'implementazione (RUE-38)**: loader stretto (campi ed effetti sconosciuti rifiutati con errore che nomina il file); calendario *time-aware* — gli effetti `SetCalendar` sono compilati in **emendamenti datati** (festività / giorno di riposo con tick di decorrenza), così il calendario resta config immutabile e mai stato mutabile, e load/replay lo ricostruiscono identico. Solo `SetCalendar` è cablato end-to-end; gli altri effetti del vocabolario sono **riservati** (rifiutati con messaggio dedicato) finché non arriva l'evento di slice che li richiede. L'hash di scenario (RUE-8) è esteso all'**intero bundle** (config scenario + mappa + definizioni): moddare la timeline cambia l'hash. Pacchetto singolo; l'ordine di caricamento multi-pacchetto è RUE-36. Committato `base:milano-1880` che riproduce **esattamente** il calendario hardcoded (golden invariato).

**Vocabolario chiuso degli effetti** (append-only; si estende quando arriva il contenuto che lo richiede):

- `SetCalendar` — cambia festività o settimana lavorativa da quella data (il sabato che diventa festivo negli anni '20);
- `SetCarrierAvailability` / `SetProducerParam` — override di un parametro di definizione dalla data (autarchia che apre nuovi flussi di recupero; guerra che ritira l'autocarro e riabilita la navazza);
- `ScaleParam` — moltiplicatore in basis points su un parametro nominato (costo carburante ×N in guerra);
- `RequireNorm` — vincolo normativo con scadenza e penale (obbligo inceneritore): è lo *scripted* che arma il gate; l'ispezione stocastica (RUE-32) lo verifica;
- `GrowWorld` — muta mappa/produttori (nuovi quartieri, densità): **è così che lo script di crescita città diventa dato** (vedi sotto).

Determinismo garantito: gli effetti si applicano al confine del tick nello step calendario/eventi, in ordine dichiarato; nessuna trascendenza, nessun RNG negli scriptati. Modificare la timeline = partita diversa (hash di scenario) = save/replay restano onesti.

**Rapporto con RUE-9 (pipeline mappe)**: RUE-9 ha reso la mappa un `*.map.json` referenziato per id. Lo scenario diventa il **bundle di primo livello** che *referenzia* quella mappa e aggiunge calendario + timeline + settings; l'hash di scenario (RUE-8) diventa l'hash del bundle (mappa inclusa). Un pacchetto mod fornisce uno scenario intero.

**Rapporto con lo script di crescita città (§15.1)**: chiarito — la crescita **non è un secondo sistema**, è una famiglia di effetti di timeline (`GrowWorld`) sullo stesso meccanismo. Il generatore (§11) produrrà scenari — cioè mappe *più* timeline di crescita — non solo mappe. Resta aperto *cosa* generare (le traiettorie), non *come* rappresentarle.

**Bounds temporali e fine opzionale** *(2026-07-19)*: lo scenario dichiara l'inizio (`calendar.epoch`) e una **fine opzionale**. La fine è una condizione di scenario (obiettivi §12), non un vincolo del motore: assente = **sandbox infinita** di prima classe (si continua a giocare dopo aver "vinto"). Il motore è comunque *year-agnostic* (tick = giorni int64; costo per-tick costante con l'età della partita) e regge qualunque durata realistica; l'unico limite strutturale è l'anno `int32` di `SimDate`. Sotto il cofano si impone un **cap artificiale al 31 dicembre 12345** (oltre, `Advance` rifiuta): assurdamente lontano per il gioco, protegge i contatori cumulativi (§15.10 → `Int128`) e rende il limite esplicito invece che accidentale.

**Non blocca la slice**: con un solo scenario `base:milano-1880` la timeline può essere vuota o minima (una-due norme igieniche). Implementazione operativa in un ticket dedicato quando arriva il secondo scenario o il primo evento storico vero; l'`onCondition` si concretizza solo se un evento della slice lo richiede.

### Formato pacchetti mod, ordine di carico e override *(deciso 2026-07-20 — RUE-36; implementato — RUE-40)*

**Decisione: chiude la regola 2 di «Moddabilità» — un pacchetto è una cartella con manifest + contenuto; N pacchetti si caricano in ordine deterministico derivato dalle dipendenze; l'override è sostituzione di intera entità per id, last-writer-wins; l'hash di scenario diventa l'hash dell'insieme ordinato dei pacchetti.** Solo contenuto (dati); le mod di codice restano post-V1 (regola 3).

**Pacchetto** = cartella autocontenuta, contenuto scoperto per convenzione (porta un subset qualsiasi):

```
data/packages/<pkg>/
  package.json                                  // manifest
  definitions/{carriers,waste,producers}.json
  maps/<name>.map.json
  scenarios/<name>/scenario.json
```

**Manifest** (`package.json`, unità intere, `formatVersion`): `{ id, name, version (semver), author?, description?, gameVersion, dependencies: [ {id, minVersion}… ] }`. L'**`id` del manifest È il namespace**: ogni id di contenuto del pacchetto deve essere `id:nome` — lega al manifest la regola namespacing (§«Moddabilità»). Il pacchetto `base` è un pacchetto come gli altri, di norma radice del grafo.

**Override / merge**:

- Caso normale = **addizione pura**: gli id namespaced non collidono tra pacchetti diversi, quindi una mod che aggiunge `mymod:autocarro-elettrico` non tocca nulla.
- **Override = sostituzione dell'intera entità per id, last-writer-wins nell'ordine di carico.** Niente patch a livello di campo in V1: ogni entità finale è un record completo e validato (determinismo e validazione restano semplici). Il patch di campo è un'estensione futura come *tipo di pacchetto* esplicito, senza rompere il replace. Nessun merge di collezioni interne (la `production` di un archetipo si sostituisce in blocco).
- Una mod può dichiarare id in un namespace **non suo** (per overridare `base:gerla`) **solo se dichiara la dipendenza** dal pacchetto proprietario — così l'override è ordinato dopo la base. Id cross-namespace senza dipendenza = errore (niente collisioni accidentali né ordine indefinito). Id duplicato nello stesso pacchetto = errore (già nei loader).

**Ordine di carico**: **derivato deterministicamente** da (insieme pacchetti + versioni + dipendenze), mai dall'ordine del filesystem o dall'orologio. **Sort topologico del DAG delle dipendenze**, con tiebreak stabile per id (ordinale) tra pacchetti indipendenti. Ciclo di dipendenze, dipendenza mancante o `minVersion` non soddisfatta = errore che nomina i pacchetti.

**Hash di scenario esteso (RUE-8/RUE-38)**: l'header del save registra l'**insieme ordinato dei pacchetti** — per ciascuno in ordine canonico `(id, version, hash-contenuto)` — più l'id dello scenario attivo. Il bundle-hash di RUE-38 (config scenario + mappa + definizioni) diventa questo *package-set hash*. Il save memorizza anche la lista `(id, version)` così il load nomina con precisione quale pacchetto manca o non combacia. L'hash per-pacchetto usa lo stesso hashing canonico ordinato di oggi; il fold aggiunge l'ordine di carico.

**Distribuzione (forma Steam Workshop)**: una cartella-pacchetto è zippabile ed è già la forma di un Workshop item (mapping id-pacchetto ↔ id-workshop, versione dal manifest). Le mod solo-contenuto sono dati puri (nessuna esecuzione di codice) → caricamento sicuro e automatico. La UI di gestione (abilita/disabilita, riordina entro i vincoli di dipendenza) vive lato Godot (post-V1); il formato la supporta già.

**Ricadute sui loader e sul save (ticket operativi)**:

- **Loader definizioni (succ. RUE-12)**: modo multi-pacchetto — carica e valida ogni pacchetto in isolamento, poi **merge in un unico registry** (replace-by-id nell'ordine di carico) con un **pass finale di cross-reference sul set unito** (un produttore mod può riferirsi a un rifiuto `base:`). Regola namespacing legata all'`id` del manifest.
- **Loader mappe (succ. RUE-13)**: mappe scoperte per pacchetto, id namespaced; il campo `map` di uno scenario si risolve sul set unito — qui atterra anche il **bind-check `map` ↔ `MapId`** annotato in RUE-38.
- **Loader scenario (RUE-38)**: i pacchetti richiesti da uno scenario diventano espliciti (dipendenze del pacchetto scenario); il bundle-hash diventa il package-set hash.
- **Save header (succ. RUE-18)**: lista ordinata `(id, version)` + package-set hash; il load verifica il set caricato e nomina il pacchetto colpevole.

**Fuori perimetro**: mod di codice (post-V1, §«Moddabilità» regola 3); patch a livello di campo; UI di gestione mod (con la UI, RUE-17+).

**Non blocca la slice**: con il solo `base` il loader multi-pacchetto è un pacchetto radice singolo — la struttura `data/packages/base/` e il merge si costruiscono quando arriva il secondo pacchetto o la prima mod (ticket operativo dedicato).

**Stato d'implementazione (RUE-40)**: contenuto base spostato in `data/packages/base/` (definitions/maps/scenarios) con manifest `package.json` (`id`, `name`, `version` semver, `gameVersion`, `dependencies`). `ContentLoader` scopre i pacchetti, valida le dipendenze (esistenza + `minVersion`), ordina con sort topologico deterministico (dipendenze prima, tiebreak per id), fonde le definizioni **replace-by-id** e risolve il cross-reference archetipo→rifiuto sul set unito; la regola di namespace (id dichiarabili solo nel proprio namespace o in quello di una dipendenza) blocca gli override cross-namespace non dichiarati. Mappe e scenari fusi per id; il `map` di uno scenario è bind-checkato sul set unito. Il save (container v2) porta lo scenario attivo + la lista ordinata `(id, version)` e un hash del set; il load confronta la lista elemento per elemento e nomina il pacchetto mancante o a versione errata. **Scostamento minore dalla decisione**: l'hash del set è `(id, version)` ordinati + hash del contenuto *unito* (non un content-hash per-pacchetto) — stessa identità e stesso fallimento preciso, meno codice. Golden invariato.

---

## 3. Produttori e rifiuti

- **Produttori a livello di aggregato urbano**: un condominio/negozio/azienda produce *y* rifiuti ogni *z* tick. **Niente agenti individuali** alla Cities: Skylines — costano tanto e non aggiungono profondità decisionale. Stesso principio per graffiti, verde ecc.
- **Ogni produttore emette più tipologie di rifiuto** → deve essere servito da una molteplicità di raccoglitori/mezzi.
- **Due vincoli di raccolta** per produttore:
  - buffer di accumulo (capacità/volume);
  - intervallo massimo sanitario (casa singola: almeno 1/settimana; condominio da 52 famiglie: quotidiano nei giorni lavorativi, per spazio e quantità).
- **Calendario sopra i tick**: giorni lavorativi e festivi (il lunedì post-domenica è pesante), stagionalità (feste, caldo estivo che stringe i vincoli sanitari), scioperi.

La combinazione produttore-aggregato × multi-frazione fa sì che la **storia stessa sia la rampa di complessità**: mucchio indistinto (un flusso) → bidoni (capacità e sostituzione) → differenziata 1980 (problema multi-commodity sulla stessa mappa). Il gioco si insegna da solo giocando la storia.

**Ontologia degli oggetti di gioco** *(annotato 2026-07-18)*:

- **Produttore, vettore (carrier) e consumatore sono ruoli componibili, non gerarchie di classi.** Un'entità può cumularli — la fabbrica di moka dell'endgame (§8) consuma alluminio, produce caffettiere e produce rifiuti — e ogni entità ha un **proprietario** (giocatore, rivale AI, città). «Giocabile» è un attributo di proprietà, non una classe base: niente `IPlayable`.
- Il **«mezzo» è il vettore in senso lato**: lo spazzino con la gerla è un vettore quanto la navazza o l'autocarro. La composizione gerle→carretto (§7: gli spazzini scaricano nel carretto) si modellerà come **composizione di unità di raccolta** quando arriverà la meccanica, non con tipi speciali.
- **Selezione e cliccabilità sono del renderer (Godot), mai della sim**: niente `IClickable` in `Ruera.Sim`. L'ispezione passa dalle query di lettura (RUE-19); le «azioni disponibili» su un'entità sono i **comandi validi in quel momento** (la validazione dei comandi esiste già, RUE-15) — ogni mutazione resta un comando al confine del tick.
- Le **linee di servizio** (§4: i giri come template con nome) sono l'oggetto gestionale che aggrega i totali per rotta; i totali cumulativi di vita partita restano soggetti alla nota §15.10 (accumulatori a 128 bit).

---

## 4. Copertura a pittura

Meccanica centrale di assegnazione delle rotte, stile janitor di RollerCoaster Tycoon.

- Il giocatore **pittura l'insieme di vie da coprire per ogni mezzo** (copertura non ordinata — quali vie, non in che ordine).
- Il motore calcola un **giro deterministico greedy** (nearest-neighbor dal deposito) e lo mostra con frecce. Volutamente subottimo: il giro ottimo è NP-hard e *non va risolto* — migliorare spezzando le zone è il mestiere del giocatore.
- **Colore dell'intera rotta = % di budget tempo consumato** (verde → giallo → rosso a 0, contando andata, riempimenti e ritorno). Non un gradiente lungo la pittura: stabile mentre si ripittura.
- **Anteprima sempre pessimista**: mostra il costo pieno, ignora le sovrapposizioni. Il risparmio da sovrapposizione (il primo camion che arriva svuota, il secondo guadagna tempo) si materializza solo in esecuzione, come slack. Regola generale del gioco: *le stime sono pessimiste, la realtà può solo essere migliore.*
- I **suggerimenti del motore** sono astratti: ignorano le sovrapposizioni. Coprire è compito del giocatore, non del motore.
- **Scala urbana** (gerarchia, non pennello più grande):
  - i giri sono **template con nome** — le «linee di servizio» — salvati e schedulati sul pattern settimanale ("Giro Navigli: camion 3 e 7, lun/gio"); la linea è anche l'unità di rendicontazione (totali per rotta);
  - a scala di quartiere si assegna un mezzo a una **zona** e lo scheduler (semplice, deterministico, leggibile) la riempie; la pittura via-per-via resta come override;
  - la crescita storica fa da tutorial: nessun giocatore affronta mai "città enorme + strumento nuovo".
- **Effetto emergente da proteggere**: andata/ritorno dal deposito sono costo fisso crescente con la distanza → le zone lontane diventano rosse → il giocatore apre depositi satellite. Nessuna meccanica deve aggirarlo (no teletrasporti, no costi forfettari).

**Filosofia**: il motore esegue, il giocatore ottimizza (frequenze, zone, flotta, depositi).

---

## 5. Viste grafiche

Due viste, entrambe letture dello stesso motore:

1. **Vista astratta**: punti che si muovono su linee, pura astrazione. Costruita per prima; fa anche da vista tattica e debug.
2. **Vista isometrica**: scena in vero 3D con **camera ortografica bloccata** e asset 3D low-poly (niente pre-rendering di sprite: rotazione a 4/8 viste, illuminazione giorno/notte gratis — la raccolta 2–10 è notturna).

Niente 3D a camera libera.

*Nota produzione*: competenze artistiche in casa (compagna laureata in 3D animation, autore con esperienza di design 3D) + asset acquistati dove serve, sostituibili in seguito. Il budget artistico **non è considerato un rischio**.

---

## 6. Modalità e scale temporali

Tre modalità = **tre compressioni temporali dello stesso motore**. Lo **strato decisionale è identico ovunque** (appalti, flotta, pittura, assunzioni, R&D, prezzi); cambia solo la granularità di esecuzione.

| Modalità | Scala | Note |
|---|---|---|
| **Iperrealistica** | ~2,5 min/giorno, fino a 8x | Esecuzione headless possibile. Per orizzonti brevi (giorni/settimane). Base della futura versione B2B. |
| **Simulazione** | ~1 h/anno (≈10 s/giorno), accelerabile fino a 8x o rallentabile | Stile Transport Tycoon. Il ciclo giornaliero non è osservabile: si vedono flussi e piani. |
| **Arcade** | ~20 min/anno | I parametri collassano in calcoli mensili/bisettimanali. L'economia è staccata dalla resa grafica, ma è **aggregazione del motore vero** (tick raggruppati), non un secondo sistema economico. |

---

## 7. Progressione storica

Timeline di riferimento (basata su Milano; le altre città usano più o meno gli stessi periodi). **Quanto segue è un sunto**: il dettaglio per epoca va sviluppato con le fonti.

- **1874**: primo inceneritore (destructor).
- **~1880**: inizio partita. Ruée e navazzari privati. Terreno con capannone di lamiere in zona non centrale, pochi soldi.
- **Fino a ~1950**: gerle — i produttori buttano tutto in un mucchio indistinto, i raccoglitori entrano fisicamente nei condomini "a gerlate". Ogni spazzino può scaricare la gerla in un carretto (servono ≥2 dipendenti per punto di raccolta) per accumulare più spazzatura per viaggio.
  - *Da arricchire con la storia vera*: motorizzazione anni '20 (dal cavallo al camion), autarchia anni '30 (recupero materiali strategico e obbligatorio), guerra (penuria carburante — i cavalli tornano —, macerie, bombardamenti).
- **Anni '50**: bidoni in metallo nei condomini (spesa a carico dell'azienda), sostituzione pieno/vuoto, igienizzazione in azienda. Gli inceneritori iniziano a recuperare energia elettrica.
- **1968**: sacchi in polietilene; cambiano i mezzi di raccolta. (1970: Milano ~13 t/giorno.)
- **Fine anni '60**: più inceneritore, cresce il secco rispetto all'umido.
- **1980**: raccolta differenziata. Costa di più in mezzi e macchinari dedicati, ma rende di più; si risparmia in personale di cernita. Il prezzo di vendita del materiale varia con la **purezza**.
- **2020+**: sostituzioni di nuova generazione *(dettaglio perso, da recuperare)*.
- **2025+**: servizio di pulizia del materiale orbitale.
- **2030+**: impianti all'avanguardia con **sistema di ricette chimiche** (es. sciogliere le plastiche blu, raccoglierle, riciclarle).
- **2050+**: recupero e rigenerazione/trattamento delle scorie radioattive per il "riciclo".

**R&D**: investendo in ricerca e sviluppo si possono **anticipare** fasi storiche o macchinari.

**Tono**: anche il futuro resta "gestionale sporco", non fantascienza pulita. Le ricette 2030+ sono la stessa meccanica delle ricette portata all'estremo, non un'appendice sci-fi.

**Ritmo**: il tech tree storico è denso 1950–2030 e povero prima. Rimedi decisi:
- **scelta dell'anno di partenza** per scenario (es. Milano 1880 / 1950 / 1980);
- **arricchimento del 1900–1950 con la storia vera** (vedi sopra).

---

## 8. Economia

*Cadenze di pagamento (salari, appalti, manutenzione, multe) e momento di materializzazione: decise in §2 «Risoluzione al tick e cadenze economiche» (RUE-6).*

### Ricavi
- **Appalti condominiali** (fonte principale nei primi periodi).
- **Vendita di materiale** smistato/differenziato — il prezzo dipende dalla **purezza** del materiale ottenuto.
- **Bandi pubblici** (rinnovo soggetto a indice di efficienza).
- **Endgame — integrazione verticale**: comprare aziende produttrici (es. una fabbrica di moka) e chiudere il ciclo dei materiali — riciclare l'alluminio nella propria fabbrica e guadagnare anche dalla vendita delle caffettiere. Catene di produzione alla Anno. È la dimostrazione giocabile della tesi del gioco.

### Costi
- Acquisto e **manutenzione** di mezzi e macchinari; igienizzazione dei bidoni.
- Salari (esterna: raccolta; azienda: cernita, vendita, R&D).
- **Tasse e multe**. Le multe normative scattano se non ci si adegua a una norma entro *x* tempo (es. inceneritori).

### Migliorie acquistabili
- **Esterna**: ruere (gerle), navazzari (carretti), biciclette, camion, autocompattatori… — ognuna con **capacità e tempo di riempimento** propri.
- **Azienda**: nastri trasportatori, contenitori, macchinari a ciclo continuo (inceneritore, tritarifiuti, compattatori…).

### Pressioni di lungo periodo (da progettare)
Contro lo snowball late-game: minaccia di municipalizzazione, ondate normative, inflazione dei costi, rinegoziazione appalti al ribasso.

---

## 9. Crimine e giustizia

- La parte illegale è **disponibile al giocatore**: illegal dumping, corruzione, ecc.
- Ispezioni di polizia, giudici, processi, **galera**. Non finire in galera fa parte del "vincere".
- I **rivali possono fare segnalazioni** alla polizia se hanno sospetti; le segnalazioni false hanno conseguenze per chi le fa. Il sistema è simmetrico: anche il giocatore può segnalare (serve una meccanica di indizi/prove: cosa genera sospetto).
- **V1 — catena semplice**: azione illegale → accumula evidenza → ispezione (casuale o su segnalazione) → multa/processo/galera. **Reputazione = un singolo numero** che modula appalti e frequenza ispezioni.
- Approfondimenti (giudici corrotti, avvocati, ricorsi) → `Ruera 2`.

---

## 10. Avversari

- **Solo AI** in V1. PvP sincrono **eliminato**.
- **Ghost PvP asincrono**: stesso scenario e stesso seed, classifiche, replay altrui come "ghost". I ghost vanno etichettati con la versione del motore (le patch rompono i replay).
- **Visibilità dei rivali**: il desiderio è vedere i camion rivali in città (non i loro tragitti), ma la complessità è alta. Decisione rimandata alla fase AI; probabile che `Ruera` mostri i rivali solo attraverso eventi/appalti/news/bilanci (AI a formule sul livello aggregato) e i camion visibili arrivino in `Ruera 2`.

---

## 11. Modalità di gioco e contenuti

### Modalità storia
Città-scenario prestabilite (geografia, densità abitativa, tipologia di territorio reali), con **scelta dell'anno di partenza**. Assi di difficoltà: orografia × densità × ostilità del territorio.

| | Città | Territorio | Densità | Ostilità | Difficoltà |
|---|---|---|---|---|---|
| **Piccole** | Lugo | pianura | media | non ostile | facile |
| | Celenza Valfortore | collinare | media | mediamente ostile | media |
| | Pejo | montagna | bassa | ostile | difficile |
| **Medie** | Bologna § *Rusco* | pianura | bassa | mediamente ostile | facile |
| | Milano § *Ruera* | pianura | alta | poco ostile | media |
| | Genova § *Rumenta* | mare e monti | media | ostile | difficile |
| **Grandi** | Berlino | pianura | media | poco ostile | facile |
| | New York | costiero | alta | mediamente ostile | media |
| | Sydney | collinare | bassa | ostile | difficile |

Le città **non sono tutte disponibili subito**: rilascio progressivo post-lancio (dopo ~6 mesi di stabilizzazione), con il **generatore casuale** come valvola. Il generatore è il prodotto; le città storiche sono seed curati e calibrati.

### Gioco libero (simulazione)
Regione con un grande centro urbano e centri satellite; la regione può avere altre città importanti con propri satelliti.

### Arcade
Vasta area alla Simutrans con entità urbane di ogni tipo e territori variegati.

### La città cresce
La città evolve nei 170 anni (popolazione, quartieri, eventi storici). **Sistema da progettare** (direzione: scenario = mappa + script di crescita; il generatore dovrà produrre traiettorie, non solo mappe). Discussione rimandata. *Rappresentazione decisa* (RUE-20, §2 «Scenario e timeline storica»): la crescita è una famiglia di effetti di timeline (`GrowWorld`), non un secondo sistema; resta aperto cosa generare, non come rappresentarlo.

### Pipeline delle mappe e formato file *(deciso 2026-07-17 — RUE-9)*

**Decisione: il formato file è il contratto; ogni sorgente vi compila.** Authoring a mano, importer, generatore: tutti producono lo stesso `*.map.json`, e il motore (RUE-13) carica solo quello.

**Formato** (JSON, `formatVersion`, unità intere, parsing `InvariantCulture` — §2):

- `nodes`: id densi, coordinate **intere in metri** su griglia locale — *solo presentazione* (staging §5, UI di pittura §4);
- `edges`: id, `from`/`to`, **`lengthMeters` autoritativo per i costi di viaggio** — la geometria non entra mai nei costi («viaggi parametrici, niente fisica», §2). Non orientati in V1 (carri e gerle: niente sensi unici; estendibile con un flag);
- `depots`: riferimento a nodo (andata/ritorno dal deposito come costo fisso emergente, §4);
- `producers`: riferimento ad arco + `archetype` (id risolto sui dati di RUE-12).
- Il loader valida: id univoci, riferimenti esistenti, grafo connesso, lunghezze positive.

**Pipeline per la slice Milano 1880–1930: mappa d'autore dalle fonti storiche** (riferimenti in coda al documento), aggregata alle vie principali — il livello produttore-aggregato (§3) non richiede fedeltà al singolo isolato. Si costruisce **dopo** che il motore gira sulla toy map.

**OSM scartato per la slice**: la rete attuale non è la Milano del 1880 (cerchia dei navigli scoperta, corpi santi), e la curatela storica supererebbe il costo dell'authoring a scala aggregata. Il formato resta neutrale: un importer OSM→formato può arrivare per il gioco libero, e il **generatore** («le città storiche sono seed curati», sopra) emetterà lo stesso formato.

**Toy map committata**: `data/maps/toy.map.json` — griglia 4×3 (12 nodi, 17 archi con lunghezze leggermente irregolari), 1 deposito d'angolo, 6 produttori su 4 archetipi placeholder (`base:condo-small`, `base:condo-large`, `base:shop`, `base:factory` — definiti per davvero con RUE-12; id namespaced per la regola di moddabilità, §2). Fixture per RUE-13/16: basta per giri nearest-neighbor, costi di andata/ritorno dal deposito e accumulo dei non serviti.

La mappa è **dato di scenario**: entra nell'hash dei dati di scenario nell'header dei salvataggi (§2 «Save e replay: formato»).

---

## 12. Vittoria, sconfitta, ritmo

- **Nessuna condizione di vittoria globale**: di norma *non fallire è sufficiente*. Gli scenari possono avere obiettivi propri.
- Sconfitta/pressioni: finire in rosso, perdita di appalti, multe (es. per inquinamento), galera.
- **Ritmo di gioco**: flusso di micro-sollecitazioni tra una decisione e l'altra — imprevisti, scioperi, bandi a scadenza, ispezioni, guasti. (Dettaglio da sviluppare, l'impianto è previsto.)

---

## 13. Servizi accessori

Manutenzione del verde, pulizia muri/strade, ritiro ingombranti (privati e pubblici).

**Decisione**: rimandati a `Ruera 2`, oppure presenti in V1 **solo come bandi** con requisiti minimi di partecipazione (es. ≥100 camion e ≥250 dipendenti per accedere al bando) — soldi e reputazione contro consumo di capacità, nessuna meccanica dedicata.

---

## 14. Fuori perimetro / rimandato

| Cosa | Destino |
|---|---|
| PvP sincrono | Eliminato |
| Camion rivali visibili in città | Fase AI / probabile `Ruera 2` |
| Servizi accessori come meccaniche complete | `Ruera 2` (o bandi astratti in V1) |
| Sistema legale approfondito | `Ruera 2` |
| Versione B2B forecasting | Dipende dal successo del gioco; oggi impone solo motore deterministico e data-driven |
| Vista 3D a camera libera | Mai / da valutare in futuro |

---

## 15. Questioni aperte

1. **Script di crescita della città** e generatore di traiettorie (rimandato, da progettare prima degli scenari). *Rappresentazione decisa* (RUE-20): effetti di timeline `GrowWorld` (§2 «Scenario e timeline storica»); resta aperto il *cosa* generare, non il *come*.
2. **Dettaglio del sistema eventi** (tipi, frequenze, ritmo delle interruzioni per modalità).
3. **Pacing fine delle epoche**: quali anni di partenza per quali scenari; eventuale compressione elastica delle epoche povere.
4. **UI multi-frazione** post-1980: layer/filtri per frazione sulla mappa di pittura (da considerare nel design UI fin dall'inizio).
5. ~~Determinismo tecnico~~ — **deciso** (RUE-7, 2026-07-17): unità intere a 64 bit, niente virgola mobile nella simulazione; vedi §2 «Determinismo: strategia».
6. **Dettaglio scheduler** di riempimento zone e regole di priorità.
7. Recupero del dettaglio perso sul "2020+: si possono sostituire…".
8. ~~Risoluzione degli effetti~~ — **deciso** (RUE-6, 2026-07-17): tutto si materializza al confine del tick, niente checkpoint sub-tick; i processi multi-giorno sono effetti programmati su tick futuri, la messa in scena resta alla grafica. Cadenze economiche e ordine dei sistemi nel tick in §2 «Risoluzione al tick e cadenze economiche».
9. ~~Calendario e scenario come dati, non come codice~~ — **deciso** (RUE-20, 2026-07-19): lo scenario è un pacchetto (mappa + calendario + timeline + settings) e la timeline storica è una lista di effetti tipizzati su vocabolario chiuso, non codice; `SimCalendar.Milano1880()` diventerà il caricamento di `base:milano-1880`. Vedi §2 «Scenario e timeline storica: dati, non codice». Implementazione operativa quando arriva il secondo scenario o il primo evento storico vero — non bloccante ora.
10. **Statistiche cumulative e overflow** *(annotato 2026-07-17)*: lo stato corrente sta comodo negli int64 (RUE-7), ma i contatori cumulativi di vita partita (tonnellate totali prodotte/raccolte/riciclate su 170 anni, metriche derivate che moltiplicano quantità × prezzi) vanno tenuti d'occhio. Quando arriveranno statistiche/analytics, usare accumulatori a 128 bit (`Int128`) per i totali cumulativi — non serve ora, nessun contatore cumulativo esiste ancora.

---

## 16. Percorso di sviluppo

**Primo passo: vertical slice — Milano 1880–1930, modalità storia.**
Gerle, navazzari, prima motorizzazione, appalti condominiali, cernita manuale, un evento normativo/igienico. Se quel cinquantennio è divertente, il progetto sta in piedi.

**Perché uno scenario curato e non il generatore casuale**: il generatore (mappa + traiettorie di crescita, §11 «La città cresce») è un problema più grande e ancora da progettare (§15.1). Uno scenario curato dà contenuto noto e limitato — mappa fissa, produttori noti, tech tree noto — sufficiente per giudicare se il *loop* è divertente, oltre a essere una fixture stabile per i test del motore. Il generatore si affronta dopo, quando c'è un loop validato da generalizzare.

Ordine di costruzione:
1. Motore a eventi discreti deterministico (tick = giorno)
2. Vista astratta (punti su linee — anche debug)
3. Meccanica di pittura + scheduler leggibile
4. Economia base (appalti, costi, accumulo/violazioni)
5. Eventi essenziali
6. Vista isometrica

---

## Riferimenti storici

- <https://twbiblio.wordpress.com/2015/04/01/il-villaggio-degli-spazzini-in-frazione-rottole/>
- <http://www.storiadimilano.it/citta/milanotecnica/rifiuti/rifiuti.htm>
- <http://www.pronaturanovara.it/docs/cittadini-del-parco/i_rifiuti_nella_storia.pdf>
- <http://www.sanzenoopenplant.it/wp-content/uploads/2016/03/02_Marcello-Germani_Evoluzione-dei-sistemi-di-raccolta.pdf>
