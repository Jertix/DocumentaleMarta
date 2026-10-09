# Novità

Le versioni, dalla più recente. Ogni sezione `## <versione>` diventa il testo della release su GitHub
(vedi `tools\pubblica.ps1`).

## 1.2.0 — 10 ottobre 2026

### Nuovo
- **Un'icona per ogni area**: creando una nuova area, sotto il nome, si sceglie tra 45 icone (conti, banca, personale,
  automezzi, sicurezza…). Si cambia quando si vuole con **«Cambia icona…»** (pulsante nel dettaglio dell'area o tasto
  destro sull'area). L'icona compare nell'albero, prima del titolo e nel gruppo dell'area dentro «Archivio completati».
- **Note di completamento**: spuntando «Completato» nel form di una cartella (o nella finestra «Nuova cartella») compare
  un campo per scrivere come è andata, cosa è stato fatto o cosa resta da fare. Si salva da solo e sparisce, insieme alla
  data di completamento, se la cartella viene riaperta. Una cartella ricorrente non le copia nella successiva.
- **Scadenze a parole**: oltre 30 giorni la distanza si dice in mesi e giorni (**«Scade tra 3 mesi e 4 giorni»**, e in
  anni quando serve) invece che in soli giorni; lo stesso per le scadenze già passate («Scaduta da 1 mese e 14 giorni»).
  Vale nel form, nell'elenco «Scadenze» e nel suggerimento sull'albero.
- **Striscia animata in fondo all'albero**: ogni 3-6 minuti circa passa un cagnolino che corre, un omino che passeggia, un
  gattino, un uccellino o un operaio col casco (con un doppio clic sulla striscia ne passa uno subito). Compare solo a
  finestra attiva e si spegne dalle Impostazioni, nel nuovo gruppo **«Animazioni»**; non compare se in Windows le
  animazioni sono disattivate.

### Per chi aggiorna
- L'archivio si aggiorna da solo alla prima apertura (due colonne nuove nel database: l'icona delle aree e le note di
  completamento); i documenti e le cartelle non si toccano. Un backup fatto con la versione precedente si ripristina
  senza problemi.
- Le aree già esistenti mantengono l'icona di prima (la libreria) finché non se ne sceglie un'altra.
- Le impostazioni restano come sono; «Animazioni» parte acceso.

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
