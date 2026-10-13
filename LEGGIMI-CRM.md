# Collegamento al CRM Tastiere Digitali

Il programma parla con il CRM con **le stesse API di VHSCapture** (`/api/cattura/*`) e il suo tipo di lavoro:
il blocco **Riversaggio / Backup** della scheda del cliente, quando il supporto è quello di questo programma
(Tape2MP3 → musicassette, DiscRipper → CD; la quantità = quanti pezzi).

## Come si collega
1. Nel CRM: Controllo PC → 🔑 della postazione → copia il token (lo stesso di VHSCapture su quel PC va bene).
2. Nel programma: ⚙ nella banda in alto → indirizzo del CRM (https://crm.tastieredigitali.it), token, «Prova collegamento», Salva.

## Come lavora
- Alla partenza (REC in Tape2MP3, Estrai in DiscRipper) chiede **per quale cliente**: le schede con pezzi ancora da fare;
  in cima «▶ Continua con …» se c'era un lavoro aperto.
- Nella Coda del CRM compare la riga dal vivo: «📻 PC · Rossi · riversa la musicassetta 1ª di 3» / «💽 PC · Rossi · riversa il CD 1º di 2».
- A fine lavoro (export in Tape2MP3, fine estrazione in DiscRipper): **Fatto, conta** · **Scarta** (il pezzo non si fa: il
  totale scende, il prezzo si ricalcola) · **Non contare**. Annullato o errore → non si conta niente.
- **Cartella del cliente** (come VHSCapture): con un cliente scelto si salva dentro la sua cartella «Nome Cognome», creata se non c'è,
  nella destinazione scelta (Tape2MP3: prima della sottocartella; DiscRipper: prima di Artista\Album). Si spegne nel ⚙ del collegamento al CRM.
- **Tape2MP3, lati della cassetta**: accanto ai comandi scegli «2 lati» (di norma) o «1 lato». Con 2 lati **lato A e lato B
  sono due cose separate**: a fine lato A premi «Passa al lato B» (mette in pausa e segna la divisione), giri la cassetta e RIPRENDI
  (se riprendi da una pausa ti chiede se è il lato B). All'esportazione escono file separati: «Nome - Lato A» e «Nome - Lato B»
  (file unico per lato) oppure A01, A02… e B01, B02… (un file per brano), con il tag disco 1/2 e 2/2. La divisione si vede sulla
  forma d'onda («LATO B ▸»), col tasto destro si sposta o si toglie. A registrazione ferma «Registra il lato B» registra il B dopo.
  La musicassetta conta come fatta nel CRM solo quando c'è anche il lato B; se esporti solo il lato A non si conta niente e, quando
  poi esporti anche il B, i file del lato A vengono sostituiti (niente doppioni). Se passi a un'altra cassetta senza il lato B,
  chiede se contarla così (aveva un lato solo) o no.
- I pezzi contano nel totale dei supporti della scheda: finiti tutti, la scheda diventa «pronta». **🔢 Conteggio** corregge fatti/totali.
- Se la rete salta, gli eventi restano in coda su disco e partono da soli; nessun evento viene contato due volte.
