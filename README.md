# Tape2MP3

Riversaggio musicassette → MP3 / FLAC / WAV / M4A. Niente di più.

**Download, guida e domande frequenti:** [tastieredigitali.tech/casi-studio/tape2mp3](https://tastieredigitali.tech/casi-studio/tape2mp3/)

## Come si usa

1. **Ingresso**: scegli il lettore USB (viene proposto da solo se nel nome c'è "USB"). I VU meter si muovono già: fai partire la cassetta e controlla che la scritta a destra dica *livello OK* (mai *TROPPO ALTO*).
2. **● REGISTRA** e fai partire il lato A. La forma d'onda scorre in diretta.
3. Fine lato A: **❚❚ PAUSA**, gira la cassetta, **● RIPRENDI**. Tutto finisce nello stesso file. (La pausa automatica esiste ma è spenta: col fruscio delle cassette non è affidabile.)
4. **■ STOP**: parte da sola l'**analisi buchi**. Il programma misura il fruscio di *quella* cassetta e segnala, in arancione sul righello e nella lista in basso a sinistra, i punti sospetti: silenzio iniziale/finale, pause fra brani, buchi brevi e (in viola) cali improvvisi dentro la musica. Non taglia niente da solo.
5. Scorri i punti con **N / P** (o click sulla lista, o sulla fascia arancione). Il punto viene selezionato e zoomato; **▶ Senti** lo fa ascoltare con 3 s prima e dopo. Poi decidi: **✂ Taglia** (puoi prima ritoccare i bordi della selezione), **Dividi qui** (divisione fra brani) oppure **Va bene così**.
6. Puoi anche selezionare a mano (trascina sulla forma d'onda) e tagliare con **Canc**. I tagli non toccano il file registrato: si annullano con **Ctrl+Z** o tasto destro → *Ripristina questo taglio*.
7. Facoltativi: **Togli silenzio inizio/fine** (bandierine INIZIO/FINE), **Dividi sulle pause** (una divisione su ogni pausa fra brani), titoli dei brani nella tabella centrale.
8. Scegli formato, destinazione, sottocartella (es. `Rossi Mario\Cassetta 1`) e **⬇ ESPORTA**. Esce esattamente quello che senti: le parti tagliate vengono saltate.

## Tasti

| Tasto | Azione |
|---|---|
| Spazio | ascolta / ferma (se c'è una selezione ascolta solo quella) |
| N / P | punto da controllare successivo / precedente |
| Canc | taglia la selezione |
| Ctrl+Z | annulla l'ultimo taglio |
| Esc | togli la selezione |
| I / O | inizio / fine sul punto (anche mentre ascolti) |
| M | divisione sul punto (o a metà selezione) |
| Home / Fine | vai a inizio / fine |
| + / − | zoom |
| mouse | trascina = seleziona · Maiusc+click = estendi · rotella = zoom · Maiusc+rotella = scorri · Ctrl+rotella = zoom verticale · tasto centrale trascinato = scorri · doppio click = divisione |

## Analisi buchi: se ne trova troppi o troppo pochi

⚙ → *Analisi buchi*: **Margine sopra il fruscio** (default 6 dB: più alto = più sensibile), **Buco minimo** (0,7 s), **Pausa fra brani da** (1,5 s), cali improvvisi on/off. Poi **Rianalizza**.

## Aspetto, versione e aggiornamenti

- **Tema chiaro / scuro**: pulsante ◐ in alto a destra (Automatico → Chiaro → Scuro) oppure ⚙ → *Aspetto*. *Automatico* segue il tema di Windows e cambia da solo se lo cambi in Windows.
- **Versione**: nel titolo della finestra e in basso a sinistra. Click sulla versione per le informazioni: numero di build, commit, data, ffmpeg incluso e cartelle di lavoro.
- **Aggiornamenti**: all'avvio controlla l'ultima Release su GitHub. Se ce n'è una nuova, in basso compare "⬆ Disponibile la versione …": ci clicchi e ti apre la pagina di download. Funziona solo se il repo è pubblico; il repo si cambia in `settings.json` → `UpdateRepo`.
- **Barra in basso**: formato di registrazione, peso del file e spazio libero su disco, con una stima delle ore di registrazione che ci stanno. Diventa gialla sotto i 2 GB, e sotto 1 GB il programma avvisa prima di registrare.
- **Trascina e rilascia**: trascina un file audio sulla finestra per aprirlo.

## Destinazioni di rete

⚙ → *Destinazioni*: aggiungi percorsi tipo `Y:\Riversaggi` o `\\nas\audio\Cassette`. Utente e password servono solo se la share li chiede (la password è cifrata DPAPI sull'utente Windows). Il tasto *Prova* verifica che la cartella sia scrivibile.

## Sicurezza dei dati

La registrazione viene scritta su disco in tempo reale (`%LOCALAPPDATA%\Tape2MP3\Registrazioni`) con l'header aggiornato ogni 5 secondi: se il PC si pianta, alla riapertura il programma propone il recupero. Il file di lavoro viene cancellato solo dopo l'esportazione.

## Build

GitHub Actions (`.github/workflows/build.yml`): .NET 8 WinForms single-file self-contained + `ffmpeg.exe` (gyan.dev essentials) accanto all'exe. Push di un tag `v*` → release con lo zip.

Locale: `dotnet publish src/Tape2MP3/Tape2MP3.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` e copia `ffmpeg.exe` nella cartella di output.
