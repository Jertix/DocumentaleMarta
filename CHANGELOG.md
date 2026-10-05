# Novità

Le versioni, dalla più recente. Ogni sezione `## <versione>` diventa il testo della release su GitHub
(vedi `tools\pubblica.ps1`).

## 1.1.0 — 6 ottobre 2026

### Nuovo
- **Anteprima dei file di testo**: i file **.txt**, **.csv** e **.xml** hanno ora l'anteprima nel pannello a destra, come
  testo che si può selezionare e copiare; il doppio clic (o il pulsante in alto) lo apre a tutta finestra. I TXT vanno a
  capo, CSV e XML si scorrono di lato. Un XML scritto su una riga sola si vede con i rientri.
  Si vedono le prime righe (100 di partenza); il file continua ad aprirsi con il suo programma per leggerlo tutto.
- **Ricerca anche dentro i file XML** (opzionale). Un XML può essere molto grande, per esempio un file di log: per questo la
  ricerca nel suo contenuto è **spenta di base** e di un XML si cerca solo il nome. Dalle Impostazioni si può attivare e
  scegliere se cercare **solo nel testo** (valori scritti nel file, senza i nomi dei tag) oppure su **tutto il file,
  compresi i tag**. Cambiando la scelta, gli XML già presenti si rileggono da soli in background.
- **Impostazioni**: due gruppi nuovi, «Ricerca nei documenti» (XML) e «Anteprima dei documenti» (quante righe mostrare per
  i file di testo, da 10 a 1000).

### Corretto
- Nella casella di ricerca, mentre si scriveva, comparivano **due «✕»** sfasate per svuotare il testo: ora ce n'è una sola.

### Per chi aggiorna
- Le impostazioni e l'archivio restano come sono: basta sostituire il programma. Le voci nuove partono dai valori
  predefiniti (ricerca negli XML spenta, 100 righe di anteprima).
- I file XML già caricati restano ricercabili per nome; per cercarne anche il contenuto va attivata la ricerca negli XML
  nelle Impostazioni.

## 1.0.0

Prima versione: archivio a aree e cartelle, scadenze con avvisi, ricerca nel testo dei documenti (PDF, Word, Excel,
PowerPoint, OpenOffice e LibreOffice, scansioni con l'OCR di Windows), ricerca avanzata, anteprima di PDF e immagini,
backup e ripristino, tema chiaro e scuro con colore principale a scelta.
