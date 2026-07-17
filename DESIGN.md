# Ruera — Documento di design

> Stato: bozza consolidata delle decisioni di design. Nessun codice ancora scritto.
> Ultimo aggiornamento: 2026-07-16

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

### Ritardi realistici

- Gli acquisti (mezzi, macchinari) hanno tempi di consegna: non arrivano subito.
- Le assunzioni richiedono training: ~10 tick in cui la persona è solo un costo.
- Scopo: premiare la pianificazione, impedire il gioco puramente reattivo e l'assumi-e-licenzia.

### Requisiti trasversali

- **Salvataggio, pausa e replay integrale**: ogni partita deve poter essere salvata, messa in pausa e ri-eseguita. Gli input del giocatore sono un flusso di comandi serializzabili: partita = stato iniziale + seed + comandi. I replay servono anche a istruire le AI in futuro (Ruera 2) e a migliorare le simulazioni B2B.
- **Dati, non codice**: tipi di veicolo (nomi, capacità, tempi di riempimento/svuotamento, costi, epoche di disponibilità), tipologie di rifiuto e archetipi di produttore sono definiti in file di configurazione esterni — aggiungere un tipo non richiede ricompilare.
- **Ispezionabilità**: ogni entità (in primis i veicoli) è cliccabile e mostra il proprio stato corrente: carico vs capacità, rotta assegnata, budget consumato/residuo, avanzamento del piano del giorno.

---

## 3. Produttori e rifiuti

- **Produttori a livello di aggregato urbano**: un condominio/negozio/azienda produce *y* rifiuti ogni *z* tick. **Niente agenti individuali** alla Cities: Skylines — costano tanto e non aggiungono profondità decisionale. Stesso principio per graffiti, verde ecc.
- **Ogni produttore emette più tipologie di rifiuto** → deve essere servito da una molteplicità di raccoglitori/mezzi.
- **Due vincoli di raccolta** per produttore:
  - buffer di accumulo (capacità/volume);
  - intervallo massimo sanitario (casa singola: almeno 1/settimana; condominio da 52 famiglie: quotidiano nei giorni lavorativi, per spazio e quantità).
- **Calendario sopra i tick**: giorni lavorativi e festivi (il lunedì post-domenica è pesante), stagionalità (feste, caldo estivo che stringe i vincoli sanitari), scioperi.

La combinazione produttore-aggregato × multi-frazione fa sì che la **storia stessa sia la rampa di complessità**: mucchio indistinto (un flusso) → bidoni (capacità e sostituzione) → differenziata 1980 (problema multi-commodity sulla stessa mappa). Il gioco si insegna da solo giocando la storia.

---

## 4. Copertura a pittura

Meccanica centrale di assegnazione delle rotte, stile janitor di RollerCoaster Tycoon.

- Il giocatore **pittura l'insieme di vie da coprire per ogni mezzo** (copertura non ordinata — quali vie, non in che ordine).
- Il motore calcola un **giro deterministico greedy** (nearest-neighbor dal deposito) e lo mostra con frecce. Volutamente subottimo: il giro ottimo è NP-hard e *non va risolto* — migliorare spezzando le zone è il mestiere del giocatore.
- **Colore dell'intera rotta = % di budget tempo consumato** (verde → giallo → rosso a 0, contando andata, riempimenti e ritorno). Non un gradiente lungo la pittura: stabile mentre si ripittura.
- **Anteprima sempre pessimista**: mostra il costo pieno, ignora le sovrapposizioni. Il risparmio da sovrapposizione (il primo camion che arriva svuota, il secondo guadagna tempo) si materializza solo in esecuzione, come slack. Regola generale del gioco: *le stime sono pessimiste, la realtà può solo essere migliore.*
- I **suggerimenti del motore** sono astratti: ignorano le sovrapposizioni. Coprire è compito del giocatore, non del motore.
- **Scala urbana** (gerarchia, non pennello più grande):
  - i giri sono **template con nome**, salvati e schedulati sul pattern settimanale ("Giro Navigli: camion 3 e 7, lun/gio");
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
La città evolve nei 170 anni (popolazione, quartieri, eventi storici). **Sistema da progettare** (direzione: scenario = mappa + script di crescita; il generatore dovrà produrre traiettorie, non solo mappe). Discussione rimandata.

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

1. **Script di crescita della città** e generatore di traiettorie (rimandato, da progettare prima degli scenari).
2. **Dettaglio del sistema eventi** (tipi, frequenze, ritmo delle interruzioni per modalità).
3. **Pacing fine delle epoche**: quali anni di partenza per quali scenari; eventuale compressione elastica delle epoche povere.
4. **UI multi-frazione** post-1980: layer/filtri per frazione sulla mappa di pittura (da considerare nel design UI fin dall'inizio).
5. ~~Determinismo tecnico~~ — **deciso** (RUE-7, 2026-07-17): unità intere a 64 bit, niente virgola mobile nella simulazione; vedi §2 «Determinismo: strategia».
6. **Dettaglio scheduler** di riempimento zone e regole di priorità.
7. Recupero del dettaglio perso sul "2020+: si possono sostituire…".
8. **Risoluzione degli effetti**: tutto al tick, oppure aggregare i tick e materializzare gli effetti ai checkpoint sulla mappa (es. al rientro in azienda)? Argomento pro-tick: anche nella realtà i flussi hanno cadenze fisse (contratto firmato, paga settimanale/quattordicinale/mensile, spese distribuite nel mese).

---

## 16. Percorso di sviluppo

**Primo passo: vertical slice — Milano 1880–1930, modalità storia.**
Gerle, navazzari, prima motorizzazione, appalti condominiali, cernita manuale, un evento normativo/igienico. Se quel cinquantennio è divertente, il progetto sta in piedi.

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
