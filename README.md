# Documentale

Programma per Windows per **archiviare e ritrovare i documenti** di una piccola azienda: fatture, F24, contratti,
pratiche, scansioni. Nato per **METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC** (Genova), è tutto in italiano e
lavora solo sul computer dell'utente: nessun servizio online, nessun abbonamento.

I documenti si organizzano in **aree** (Fatture, INPS, Agenzia delle Entrate…) e **cartelle** (una per ogni pratica);
ogni cartella può avere una scadenza, e il programma avvisa quando si avvicina.

## Cosa sa fare

**Organizzare**
- Albero a tre livelli: radice → area → cartella. Ogni area e ogni cartella corrisponde a una cartella vera sul disco,
  quindi l'archivio si legge anche da Esplora file.
- I documenti si **allegano per copia**: il file originale non si tocca mai. Si possono anche trascinare da Esplora file
  sul form di una cartella, e spostare da una cartella all'altra trascinandoli sull'albero.
- Se il file che si sta allegando c'è già nell'archivio (anche con un altro nome) il programma lo dice e chiede cosa fare.
- Le eliminazioni vanno nel **Cestino di Windows**, mai cancellate definitivamente.

**Scadenze**
- Ogni cartella può avere una scadenza. Avvisi a due livelli, con soglie che si scelgono nelle Impostazioni:
  **arancione** (si avvicina) e **rosso** (vicina o già passata), con icone nell'albero, righe colorate nelle griglie e
  un nodo speciale **Scadenze** con l'elenco di tutto ciò che va controllato. All'apertura un riepilogo.
- **Cartelle che si ripetono** (ogni mese, tre mesi, anno): completando una cartella il programma propone la successiva.
- Le cartelle **completate** hanno un'icona diversa e si possono mettere nell'**Archivio completati**, un ramo in fondo
  all'albero (i file restano dove sono).

**Cercare**
- Ricerca che guarda nei nomi dei file, nei titoli e nelle descrizioni delle cartelle, nei nomi delle aree e **dentro i
  documenti**: PDF, Word, Excel, PowerPoint, **OpenOffice e LibreOffice** (.odt, .ods, .odp…), testo.
- Le scansioni e le immagini si leggono con il **riconoscimento del testo (OCR) di Windows**, in italiano.
- **Ricerca avanzata**: tipo di file, area, stato della cartella (aperte, completate, in scadenza, scadute, archiviate),
  intervallo di scadenza.

**Vedere**
- **Anteprima** nel pannello a destra per PDF, immagini e documenti OpenOffice/LibreOffice; con il doppio clic si apre
  ingrandita (zoom, scorrimento, pagine).

**Proteggere**
- **Backup** con un clic di tutto l'archivio (documenti e database) in un file ZIP, di default in
  `C:\Backup\DocumentaleMarta`. Un promemoria ricorda quando è passato troppo tempo dall'ultimo.
- **Ripristino** da un backup in una cartella nuova, senza mai toccare l'archivio attuale.

**Personalizzare**
- Finestra **Impostazioni**, sezione **Aspetto**: tema **chiaro**, **scuro** o **come Windows**; **colore principale**
  (Acciaio, Fucina, Foresta, Prugna, Grafite o quello di Windows) per pulsanti, spunte e selezioni; **sfondo delle
  finestre colorato** con una leggera tinta di quel colore. Le scelte si vedono subito, senza riavvio, e con «Annulla» il
  programma torna com'era.
- Nelle Impostazioni anche le soglie degli avvisi, i dati della ditta e il backup.

## Come si usa

1. Si apre il programma: al primo avvio crea l'archivio in `C:\Documentale` (con il database in `_dati`).
2. Con il tasto destro sulla radice si crea un'**area**, poi dentro un'area una **cartella**.
3. Nel form della cartella si scrivono titolo, descrizione e scadenza e si allegano i documenti (pulsante **Allega…** o
   trascinandoli).
4. Il campo in alto cerca ovunque; **Ricerca avanzata** restringe i risultati.
5. Dalle **Impostazioni** (ingranaggio in alto a destra) si cambiano aspetto, soglie e backup.

## Dove stanno i dati

| Cosa | Dove |
|---|---|
| Archivio (documenti e cartelle) | `C:\Documentale` |
| Database | `C:\Documentale\_dati\documentale.db` |
| Impostazioni | `%AppData%\DocumentaleMarta\impostazioni.json` |
| Backup (predefinito) | `C:\Backup\DocumentaleMarta` |

Per provare il programma senza toccare i dati veri si può indicare un altro file delle impostazioni con la variabile
d'ambiente `DOCUMENTALE_MARTA_IMPOSTAZIONI`.

## Requisiti

- Windows 10 (versione 2004, build 19041, o successiva) o Windows 11.
- Per leggere le scansioni serve il pacchetto lingua **Italiano** con il riconoscimento del testo (Impostazioni di
  Windows → Ora e lingua → Lingua e area geografica → Italiano → Opzioni lingua → Riconoscimento del testo). Senza, tutto
  il resto funziona; le scansioni restano da leggere finché non lo si attiva.
- Per compilarlo: [.NET 10 SDK](https://dotnet.microsoft.com/download).

## Per chi sviluppa

Il codice è in C# su .NET 10: WPF per l'interfaccia (stile di Windows 11 con tema chiaro/scuro), Entity Framework Core con
SQLite per i dati (la ricerca nel testo usa FTS5), `CommunityToolkit.Mvvm` per il modello MVVM.

```
DocumentaleMarta.Core    Regole e modelli, senza dipendenze da interfaccia o database
                         (avvisi, impostazioni, nomi di file sicuri, ricorrenze, interfacce dei servizi)
DocumentaleMarta.Data    Database (migrazioni), archivio su disco, ricerca, backup, lettura del testo dei documenti
DocumentaleMarta.App     L'applicazione WPF: finestre, view model, tema, servizi di Windows (OCR, anteprime)
DocumentaleMarta.Tests   Test automatici (xUnit)
tools                    Script di supporto (generazione dell'icona)
```

Compilare ed eseguire i test:

```bash
dotnet build
dotnet test
dotnet run --project DocumentaleMarta.App
```

Il progetto compila con zero avvisi (`dotnet build -warnaserror`). I test comprendono prove delle finestre WPF disegnate
in memoria (senza mostrarle) e file veri creati con LibreOffice. Per salvare come immagini PNG quello che le prove
disegnano si imposta `DOCUMENTALE_TEST_IMMAGINI` con una cartella; con `DOCUMENTALE_TEST_TEMA=Scuro` le immagini escono
con il tema scuro (le prove che controllano i colori chiari, in quel caso, falliscono).

Nuova migrazione del database (gli strumenti `dotnet ef` sono nel manifest locale `dotnet-tools.json`):

```bash
dotnet tool restore
dotnet ef migrations add NomeMigrazione --project DocumentaleMarta.Data --output-dir Migrazioni
```

## Note

Programma realizzato per uso interno di METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC. Non è associata al
repository nessuna licenza di utilizzo da parte di terzi.
