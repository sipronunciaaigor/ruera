# Ruera — Istruzioni per Claude

## Fonti di verità (una fonte per tipo di documento, mai duplicati)

- **Design tecnico e di gioco**: `DESIGN.md` in questo repo — autoritativo.
- **Prodotto** (roadmap, log decisioni, diario): hub Notion «Ruera — Hub» — https://app.notion.com/p/3a005d5d7f7281569708dbee1ec41a81
- **Lavoro eseguibile**: Linear, team «Ruera».
- **Contesto operativo per Claude**: pagina Notion «Claude's context» (sottopagina dell'hub).

## Quando leggere le fonti

- **Inizio di una sessione di lavoro**: leggi il ticket Linear in questione e le sezioni di `DESIGN.md` che linka (il file è nel repo, costo zero).
- **Prima di pianificare lavoro nuovo o proporre priorità**: pagina Notion «Roadmap e priorità».
- **Prima di rimettere in discussione una scelta**: database «Log delle decisioni» — la decisione potrebbe già esserci, con il perché.
- **Quando riprendi lavoro interrotto o ti manca contesto recente**: «Diario di sviluppo».
- **«Claude's context»** è amendabile da fabri in ogni momento: rileggila quando le istruzioni sembrano non combaciare con quello che ti viene chiesto.
- Le sessioni di pura discussione non richiedono letture preventive: leggi solo ciò che serve a rispondere.

## Routine obbligatoria di fine sessione

Se nella sessione hai sviluppato o modificato qualcosa — codice, `DESIGN.md`, pagine Notion, issue Linear — o sono state prese decisioni:

1. Aggiungi una voce al **Diario di sviluppo** in Notion (https://app.notion.com/p/3a005d5d7f7281bb8dd1ef28469799ae): data, cosa è stato fatto, cosa resta aperto. Voce più recente in alto; se esiste già la voce di oggi, estendila.
2. Se sono state prese **decisioni nuove**, registrale nel database «Log delle decisioni» nell'hub: titolo, data, area, perché, **Ticket** (ID Linear coinvolti, es. `RUE-11, RUE-20`) e **Sezione DESIGN.md** (es. `§2`) — usa `—` dove non applicabile. Se una decisione già registrata acquisisce nuovi ticket o cambia sezione, aggiorna la riga esistente invece di crearne una nuova.
3. Se il **design** è cambiato, aggiorna `DESIGN.md` — mai duplicarlo in Notion.

Le sessioni di sola lettura/discussione senza esiti non richiedono voci.

## Convenzioni

- **Lingue**: codice, commit, branch e issue in inglese; documenti in italiano; la conversazione con fabri è mista IT/EN.
- **Branch** legati agli ID Linear (es. `rue-12-tick-engine`); commit e PR referenziano le issue.
- **Prima di lavorare su un ticket**: `git checkout main && git pull` — il branch parte sempre da `main` aggiornato, mai da una copia locale stantia.
- **Architettura**: la simulazione è una class library .NET pura (nessuna dipendenza da Godot), multi-target `net9.0;net10.0`; Godot 4 (C#) fa solo da renderer/UI.
- **Determinismo obbligatorio** nella simulazione: niente casualità non seedata, niente dipendenze dal frame rate o dall'ordine di iterazione non definito. Strategia (RUE-7): **unità intere a 64 bit** — niente `float`/`double`/`decimal` in `Ruera.Sim`; RNG proprio in repo con stream per sistema (mai `System.Random`); niente `DateTime.*`, `GetHashCode`, parallelismo nella logica di stato. Regole complete in `DESIGN.md` §2 «Determinismo: strategia».
- **Micro-ticket Linear** eseguibili «a freddo»: titolo imperativo in inglese, contesto in 2 righe + link alla sezione di DESIGN.md, criteri di accettazione verificabili, file toccati se noti.
