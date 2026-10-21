# DuckNote Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.0.12] - 2026-10-21 — new interface

**A new window, built from scratch on the Duck design kit**, released as an
update of 0.0.12. Same features, same file formats: notes, containers and
`settings.json` written by earlier versions open as they were.

### Changed

- **Shell**: macOS-style window with traffic lights; at the top right the
  version, a padlock (lock now, or set up encryption), the **Impostazioni**
  gear and day/night, all the same size. Themes crossfade and can follow
  Windows live; toasts, illustrated headers and empty states, ducks swimming
  behind the pages.
- **Sidebar**: the duck with *DuckNote*; the current user (full Windows name,
  `utente su PC`, one of 26 ducks dressed up in a funny way chosen by the first
  letter of the name, a badge with the vault state). A click on the user opens
  a menu: lock the note or quit, or — without encryption — set it up.
  **Rete** with hosts down in red and up in green; then the **Fogli**, each on
  a tile of its own colour with its H1/H2 titles always at hand, a search and
  a + above them, and opening/closing animated; at the bottom the monitoring
  card with the button that checks the hosts in the sheets.
- **The sheet takes the whole window** and looks like paper: the title written
  on it renames it; words, page, last save, zoom and encryption under it.
- **The formatting bar comes from one table** (`NotesPage.Toolbar.cs`): toggles
  stay pressed where the cursor is, disabled commands are greyed out, tooltips
  carry name and shortcut, nothing wraps (the rest goes into ⋯), Alt+F10
  reaches it from the keyboard. New shortcuts (Word/Docs where they exist),
  text colour, and Markdown-style starts (`# `, `- `, `1. `, `[] `, `> `,
  ```` ``` ````). With no selection a mark applies to the word under the cursor.
- **Enter starts a clean line**, without title, list, quote, code, bold or
  colour; **Shift+Enter** keeps the formatting (next list item, next code
  line, same title).
- **Right-click** on a table, a separator, a code block or a quote deletes it;
  hosts set aside come back from the right-click menu of the sheet and of the
  Rete table.
- **Analisi and Host are one page, Rete**: table on the left with a coloured
  dot (green online, orange to check, red offline), the IP and the name; the
  card of the selected host on the right; filters by state and by origin
  (*Tutti, Nota, Ricerca*), search and the hosts set aside in one row above.
- **Full analysis once per start**: after it, the monitor and new checks only
  ping and keep names, MAC, ports and the rest; a host that comes back on is
  analysed again in full. A new range removes only the rows outside it;
  **Host della nota** keeps only the hosts written in the note.
- **Automatic scans are quiet**: no *Analisi finita* toast for the monitor or
  freshly written addresses.
- **Impostazioni** in tabs (*Aspetto, Nota, Scansione, Raccolta, Cassaforte,
  Sistema*), groups in two columns, every row with the text on the left and the
  control on the right, applied as you change them. **Cassaforte** is one of
  the tabs: a three-step wizard, then state, auto-lock, *Blocca adesso*,
  *Cambia password* and *Togli la cifratura*.
- **Unlocking** covers the whole window in one colour and shows a plain loading
  spinner while the key is derived.
- **The editor was rewritten** with the same behaviour: code blocks coloured by
  language, real tables, links, drawings, images and charts the text wraps
  around, addresses coloured by state.
- Logic that needs no window moved to `DuckNote.Notes`, with 49 tests: address
  recognition, code colouring, charts, the note file, the sheet tree, the table
  filter and CSV, settings.

### Removed

- The PowerShell original (`_old/`), editor settings (`.vscode/`) and the
  screenshots of the old interface (`docs/`) are no longer in the repository.

### Added

- **Host table: filter by state** (*Tutti, Attivi, Spenti, Da controllare*) and
  **search by field** (`ip:` `nome:` `mac:` `porta:` `os:` ...). `porta:22`
  wants port 22, not 2222.
- **CSV export** of what the table shows, with semicolons and a BOM so Excel in
  Italian opens it as is. Cells starting with `=`, `+`, `-` or `@` are escaped:
  a network banner cannot become a formula.
- **Send to the note**: the rows shown become a table in the open page.
- **Zoom**: Ctrl+wheel, Ctrl+plus, Ctrl+minus, Ctrl+0.
- **The last scan is kept** (`lastscan.json`, or inside the vault) and comes
  back at the next start, so states are there before the first check.
- **IEEE vendor list download** from Settings, kept in `oui.txt`.
- `Ctrl+K` for links, `Ctrl+Shift+V` to paste plain text.

### Fixed

- **The Rete table shows what a range scan finds**: it keeps its own list,
  rebuilt from the filters, and scan results always reach it on the interface
  thread.
- **A range scan no longer loses its rows when the note changes.** A row found
  by range is not tied to the note; only rows born from an address written in
  the note follow it.
- **`note.xaml.bak` is refreshed at most every 15 minutes**, as documented,
  instead of at every save, which made it a copy of the last few seconds.
- **Colours read from a saved note follow the theme.** Saving to XAML writes
  colours as fixed values; they are recognised on reading and tied back to the
  theme, so text written by day stays readable at night.
- **The background ducks follow the theme** and stop while you type.
- **Settings showed the wrong theme with *follow the system* on**, and Save then
  wrote `light`.
- **`settings.json` and `note.xaml` are written beside and moved into place**:
  an interruption leaves the previous file, not a truncated one.
- `SecretKey` called `kernel32`/`crypt32` unconditionally, so the vault tests
  could not run anywhere but Windows.

## [0.0.12, first build] - 2026-10-16

### Added

- **A code block is coloured by the language it speaks.** Five roles instead of
  one flat grey: the words the language keeps for itself, text between quotes,
  numbers, comments, and the names of functions — a name up against an open
  bracket, called or declared, which for a reader is the same thing.

  Nothing says which language it is. There are no fences any more, so it is
  guessed from the signs a language leaves lying around — a `def `, an `fn `, a
  `$name`, a `SELECT`: C#, Python, JavaScript, Rust, PowerShell, SQL and shell.
  When the signs are not enough the answer is *no language*, and the block stays
  as it was: colouring at random is worse than not colouring.

  Open a comment and leave it open and the next line carries on in it; a quote
  left open does not, because a line being typed always has one and it must not
  tint the whole block. Inside a block an address is code, not a host.

  Measured in the real window, because repainting a paragraph is exactly how a
  caret gets lost: typing a letter in the middle of a block repaints it and the
  caret stays after the letter. It did jump a line at first — a line break is
  crossed in two steps and counting both skips a line.

### Changed

- **The separation line takes exactly one line, and leaves you on a new one.**
  It used to take a line and a bit — 23 against a text line's 18 — and the caret
  landed at the start of whatever was written below, so you carried on inside
  that text. The rule now goes on the line right under the one you are writing,
  what was below stays untouched, and you carry on on a line of its own beneath
  it. Measured: a band of 18 against a text line of 18.

### Fixed

- **An address you had just written stayed grey.** Writing `nb.zaick.net` put a
  row in the host table as *mai controllato* and then **nothing checked it** —
  the first look came from the monitor, up to a minute later. The whole chain
  was sound (measured end to end: the name is recognised, resolves to
  57.131.13.192, answers in 9 ms with TTL 54, three ports open, and the table
  row goes green); what was missing was anyone asking. A new address is now
  checked on its own 1.6 s after you stop typing, which is also why it waits:
  `nb.zaick.ne` is as valid a name as its continuation, and is not worth
  looking up.

  FQDN recognition was measured on twelve shapes while hunting for this, since
  it was the first suspect: long names, many labels, hyphens, mixed case, a
  trailing dot, `.museum`, punycode, a port suffix, inside a URL. All correct —
  and an email address is still not a host.

- **There was no way out of a code block.** Enter added a line to the block,
  always, so once inside you stayed inside. Enter on an empty line at the end of
  the block now closes it and leaves you on a normal line below — the usual way
  a block ends — and the empty line that asked for it is not kept. Shift+Enter
  still adds a line without leaving. A block emptied this way does not stay
  behind as a frame around nothing.

- **A separation line cut straight through an image.** A paragraph's border is
  drawn across its whole width, floating frames included, so a rule beside an
  image sliced it in half. The rule now clears floating frames and passes below
  them, where a separation means something. Measured: the line at y=185 against
  a frame ending at y=183.

- **Clicking the text while renaming a page saved the name.** The click reached
  the sidebar row, the row took the keyboard focus, the field lost it, and
  losing focus is what commits.

  There is one rule now, and it turns on where you click. **On the title text**
  the click stops in the field and behaves like text: the caret goes where you
  clicked, a double-click selects it all. **Anywhere else** — the sidebar row
  included — means you are done, and the name is saved.

  Both halves are measured: the click inside the field is stopped and the field
  stays open with the half-written name intact, and moving the focus to the row
  closes it with the page named as typed. Finding this took understanding why
  the harness could not reproduce it before — **a synthetic event raised on a
  control template's child is not routed to the templated parent**, so the
  simulated click never reached the handler that was supposed to stop it.

  Losing the focus was the only thing that committed the name, and a click does
  not always move the focus. A click anywhere in the window now commits it
  directly, and the menu entry says **Modifica titolo** — it edits a title, it
  does not set one for the first time.

- **An address was turned into a host half-way through typing it.** Writing
  `nb.zaick.net` goes through `nb.zaick`, which is a perfectly good name: it was
  coloured, scanned and listed under your fingers, and the row for the address
  you actually meant arrived next to it. The address under the caret is now
  still a word being written: it becomes a host when the caret leaves it — a
  space, Enter, a click elsewhere — or **five seconds after the last key**,
  because stopping is also a way of finishing.

- **An address was coloured in half.** `welcome.zaick` went grey as a host and
  the `.net` typed after it stayed white, two looks for one address. The painter
  read one text run at a time, and the letters you type after a dressed address
  are born in a *new* run: nothing ever put the two back together again, so the
  address stayed cut where you had paused. It now reads a **stretch** of the
  line — adjacent pieces dressed alike — because an address is a word of the
  line, not a word of a piece of the line. It goes grey whole, then green or
  red whole.

  Measured in the real window, typing through the keyboard's own door: stop at
  `welcome.zaick` and it is a host; carry on with `.net` and the whole thing is
  text again, in one piece; stop again and the whole `welcome.zaick.net` is the
  host. The harness used to insert text straight into the run under the caret,
  which is not what typing does — that is why it had never seen this.

  An address does keep its colour when the caret merely sits in front of it: it
  arrives there from outside, by a click or an arrow key, and is not writing.

- **The row for the address you had just written was torn out of the table, so
  it never went green or red.** The rule about the word under the caret had
  leaked into two places it had no business in. The table was *pruned* with the list that hides that word, so
  the row born five seconds after you stopped typing was thrown away one tick
  later, and the scan result with it. And the first check *filtered* its
  candidates with the same list, so the one address you had just written —
  the one with the caret still on it — was the only one never looked up: it
  waited for the monitor's sweep, up to a minute away.

  The rule belongs to adding a row and to colouring the text. Removing a row
  and deciding what to check both read the note as it is written, caret or no
  caret. Measured with the address typed at the end of a line and the caret
  left on it: the piece is painted with the *reachable* brush and the table
  row reads `Online`.

- **Deleting an address left its row in the table.** The table is a mirror of
  what the notes say, and a row that outlives the text speaks of a host that is
  not there any more. Rows born from a note now follow their note: delete the
  address and the row goes, while rows you scanned by range stay, because
  nothing in a note was claiming them.

- **Repainting counted as writing.** Each scan result recolours the addresses in
  the text, and WPF reports that as an edit like any other. Two clocks were
  listening: the one that waits five seconds of silence never got there while a
  scan was running, and the one that saves four seconds after an edit kept
  firing — a save every four seconds, for ever, each save feeding the next,
  since taking the frames out of the document to save them is also an edit.
  Neither repainting nor saving is writing now. Measured by counting the ticks
  that arrive: one, where there were none.

## [0.0.11] - 2026-10-13

### Removed

- **The Markdown layer is gone.** Formatting was reported as a nightmare, and
  the nightmare had one source: a parser running on every keystroke. It read the
  line under the caret, decided whether a marker had closed, and rewrote the
  line — replacing the text runs the caret was sitting in. Every caret jump,
  every swallowed character, every marker that fired when it should not have
  come from there. The toolbar buttons never went through it; they already
  styled the selection directly.

  So the parser is deleted — `MarkdownRenderer`, `InputRules` and `LineParser`,
  three files and the live-formatting setting with them. What is left only
  *dresses* a paragraph into a look (`Dresser`), and the live formatter now does
  two things: colour addresses and keep custody of the caret while it does.
  Asterisks, backticks and `#` are ordinary characters now. Notes written under
  the old rules open exactly as they were — the markers were never stored.

### Added

- **Drawings, images and charts inside the text.** The last three buttons in the
  bar put a framed box where the caret is, and the bottom edge of the frame
  drags to set its height.

  - A **drawing** is an ink sheet: six colours, three nib widths, an eraser.
    Its paper and ink do not follow the theme, because a sketch made in the
    dark theme has to stay legible in the light one.
  - An **image** comes from a file, or from Ctrl+V. Pasting an image used to
    produce an `InlineUIContainer`, which does not survive being saved: the
    picture would have disappeared on reopening. The paste is intercepted and
    becomes a frame instead.
  - A **chart** is built from a few typed lines — `gateway 3`, one per line,
    with `tipo:` choosing bars, line or pie and `titolo:` naming it. It stays
    text, so a wrong number is corrected rather than redrawn, and the dialog
    shows the chart live beside what you type. Colours come from the theme.

  How it is stored was settled by measurement, not by preference. A
  `BlockUIContainer` does **not** survive `TextRange.Save` — it comes back as an
  empty paragraph, which would mean losing a drawing in silence. A
  `Hyperlink.NavigateUri` does survive. So on screen a frame is a live control,
  and on disk it is a placeholder carrying `ducknote://riquadro/<id>` with its
  kind and height, while the bytes sit beside the page in the note container:
  ink as strokes, an image as its original bytes, a chart as its text. The swap
  is reversible and puts **the same** frames back, so a save while you are
  drawing does not interrupt you. A frame whose bytes are missing says so in the
  text rather than appearing as an empty box.

### Changed

- **A frame is as wide as it needs to be, and text wraps around it.** Drawings,
  images and charts used to take the whole column. Each now floats at the left
  of the line at its own width, and the text flows past it **line after line**,
  Enter included — a new paragraph beside a frame starts beside it too, and the
  text returns to full width once the frame ends. Measured: a 300×180 frame with
  three lines of text beside it, and a following paragraph starting at x=314
  against a frame ending at x=306.

  Two things had to be right for that. A floater anchored in a paragraph that
  **already has text** does not make it wrap: it settles on the next line and
  the words stay above it, which is what "I can't write beside the image" was —
  measured, the typed word landed at x=72 while the frame spanned 39 to 399. So
  a frame takes a line of its own when the caret's line has words in it, and
  frames always go at the **head** of their line, before any text.

  Each frame has a grip on its bottom edge for height and one on its right edge
  for width, and **the width stops at the page**: dragging 5000 px out lands on
  684 in a 684 px line, because past that the frame would hang off the page and
  the text would have nowhere left to wrap.
- **Two frames next to each other share the line.** Put a second one beside the
  first and they split the column evenly, tops aligned. Measured: 405 px each,
  both at the same height. Two things had to be true for that: they have to live
  in the **same paragraph** — in separate paragraphs each is anchored to its own
  line and they come out at different heights, which is what made them look
  crooked — and the line's usable width has to be asked of the control that
  scrolls the document rather than guessed by subtracting pixels from the
  window, leaving a few spare because a line filled to the last pixel wraps.
- **A page's title is set by hand, from the right-click menu.** It used to be
  guessed from the first large heading in the text, which meant a page renamed
  itself as you wrote. *Imposta titolo* is now the only way in, and a page with
  no name is *Senza titolo*.
- **Large headings are mapped in the structure.** The tree shows `#` headings
  under their page and `##` headings under the `#` that precedes them; an `##`
  with no `#` before it stays directly under the page. A heading written in the
  text is a heading of the text, and now appears as one instead of being
  silently promoted to the page's name.
- The chart's numbers button is a purple dot showing three small bars, matching
  the red dot that removes the frame.

### Fixed

- **Text typed on a new list item came out grey.** Press a list button, press
  Enter, type: the first line was right and everything after it was grey in a
  monospaced font. The caret was left immediately after the list marker, and a
  caret inherits the formatting of the run it sits in — that grey belongs to the
  bullet, not to what you are about to write. The pending formatting is now set
  explicitly where a marker is involved, which is what WPF applies to the next
  character typed.
- **A link written from the dialog lost its last letter** — "il sito" came out
  as "il sit". Finding the end of freshly inserted text by counting characters
  is off by one, because entering a run counts as a step; the run is now built
  and placed as an object, so there is nothing to count.
- **A selected frame disappeared under the selection veil.** Selecting text
  that spans a drawing or a chart put a translucent accent layer over it, so the
  thing you were about to look at was the thing you could no longer see.
  Measured: the drawing's paper went from `#F7F8FA` to `#BB9971` the moment the
  selection covered it. The veil is an **adorner** — it renders above the
  element and its children by construction, and a `Floater` does not escape it
  either (measured: veiled the same). The only lever WPF offers is
  `SelectionOpacity`, so that is the lever: when the selection takes in a frame
  the veil goes off and the frames say for themselves that they are selected,
  with an accent ring. Selecting plain text keeps the veil it always had.

- **The separation line had all its space on one side.** A paragraph's border is
  the last thing drawn in it, so padding could only ever put space *above* the
  line, leaving it glued to the text below — measured 30 px above, 10 below. The
  line's breathing now lives in its margins, which is the one paragraph in the
  note allowed to have any, and `Flatten` leaves it alone. Measured after:
  13 above, 10 below, the 3 px being the descent of the glyphs above it. It also
  no longer leaves a spare empty line behind it: if there is already a line to
  put the caret in, it goes there.

- **Turning a ticked checkbox into another kind of list kept its grey.** A done
  checkbox is the only look that paints the *text* rather than the paragraph —
  grey and struck through, on the runs. Undressing a paragraph did not take that
  off, so converting ticked rows to a bullet list left them grey while the first
  row stayed normal. Measured by rendering: rows at `#5F6369` against `#8E8E92`.
  Now undressing removes what ticking painted.

- **The chart's bars hung below their baseline, and the labels landed on top of
  them.** The bars were drawn down to the bottom of the plot while the baseline
  sits 18 px higher, which is exactly where the category labels go — so grey
  text ended up over green, blue and orange. One baseline is now computed once
  and everything measures from it, bars and line points included, and the labels
  took the full-contrast ink. The pie's legend is centred beside the pie and
  ellipsised instead of running off the frame.

- **A frame in the note broke the editor with a cascade of error dialogs.**
  Pressing the pencil or the chart button and waiting for the autosave filled
  the screen with error windows. The cause was not in the drawing or the chart:
  removing a UIElement from a live `RichTextBox` makes WPF build an undo record,
  and to build it **it hands the element to `XamlWriter`**. `InsetBox` carried a
  public `Func<string, string?>`; XamlWriter refuses generic types, the
  exception escaped while the text container was mid-edit, and from then on
  every layout pass threw again — forever.

  Measured, in this order: a *null* property of that type serialises fine, a
  *filled* one throws, and `[DesignerSerializationVisibility(Hidden)]` makes it
  pass. So the frame's two non-saved properties are hidden from serialisation,
  which is what they always were in meaning. `ChartView.Plan` turned out not to
  need it — a sabotage with the attribute removed left the harness green — so it
  does not carry one: every guard in the code is a guard some sabotage proves
  necessary.

  The gap in the tests is worth naming: the insets harness worked on *detached*
  documents, where there is no undo stack and the fault cannot appear. The new
  harness opens the real window, presses the real buttons and waits for the
  autosave — that is where it reproduced, 1296 red checks' worth.

- **The toolbar did not fit.** 28 buttons wanted 962 px in 883, so it scrolled
  sideways and the last three — the new drawing, image and chart — were the ones
  off the edge. The five table commands now live under one **Tabella** menu:
  they are a family, and four of them only mean anything with the caret inside a
  table. 24 buttons on one row, nothing cut off, nothing to drag. The row also
  wraps instead of clipping if the window is made genuinely narrow.

- **The caret in the find box sat where the text was not.** Measured: caret at
  x=21 while the placeholder started at x=10. The field template applied
  `Padding` as the content host's margin, and the TextBox inset its text by
  `Padding` again — the same nine pixels twice. One is enough: the caret is now
  at 12, next to the writing. Every field in the app was affected.

- **The link button did nothing without a selection.** It wrote "select the
  text to link" in the status line at the bottom of the window, which is not
  something anyone notices. Now the dialog opens either way: with a selection
  the text is shown and read-only, without one it is a field to type, and
  accepting writes the link where the caret is.

### Changed

- The button that removes a frame is the same red dot that closes a window,
  showing its cross when the pointer is on it, instead of the word *togli*.
- One walk through the document instead of two. `NoteDocument.Deep` descends
  lists, tables and sections once, and everything that looks for paragraphs,
  frames or a block's siblings filters that.
- Locking now zeroes the bytes of every frame along with the rest.

## [0.0.10] - 2026-10-11

The version is pinned here on purpose: work accumulates under 0.0.10 until there
is a reason to cut a new one. Four more numbers were minted during one day of
work and then withdrawn — this entry is the whole of it, newest first, with the
earlier rounds of the same day kept as sections rather than as versions.

### Fixed

- **The caret jumped to the start of the line after typing an address.** Write
  an IP or a domain name, press space, and the cursor was at column zero. Nothing
  to do with the space: 180 ms after the keystroke the live formatter repaints
  the line, and colouring an address means **replacing the runs it sits in** —
  the original run is removed and copies are inserted around the address. The
  caret was inside that run, so it was lost, and WPF put it back at the start.
  Measured: the offset went from 23 to 0, and only on lines that contain an
  address — a line without one came through untouched, which is exactly what you
  reported.

  The caret's character offset in the line is now taken before the repaint and
  put back after. The two halves — measuring an offset and finding the position
  for one — walk the text runs the same way, from one shared pair of functions,
  so they cannot drift apart. The input rules already had their own copy of that
  walk; they use the shared one now.

- **Addresses deleted from the note left their rows behind.** `SyncNoteHosts`
  only ever added: typing an address created a pending row, deleting it left the
  row in the host table and in the sidebar for good. The note's addresses are now
  pruned as well as added — but only the rows that were **never checked**. A row
  carrying a real result is scan history and stays, whether or not the note still
  mentions it.

### Security

Asked for, and the review found more than it was asked about.

- **No single-instance guard.** Two DuckNote processes could run at once, each
  with its own vault session writing the same `store.bin` — last writer wins,
  and a torn write is one explanation for the corrupted container the readme
  tells you to restore from `.bak`. Worse for the lock: **locking one instance
  left the other open and readable**, which defeats the point. A lock file in
  the data folder, opened without sharing and deleted on close, now refuses the
  second copy. It is keyed to the data folder rather than to the machine, so two
  instances on two different `DUCKNOTE_HOME` folders are still allowed — they
  share nothing.

  The first attempt used a named mutex, and the harness caught it: a mutex is
  re-entrant for the thread that owns it, so a second claim inside one process
  succeeded and one release left it held.

- **Only the open page was discarded on lock.** `Editor.Document` was replaced
  with an empty one, but the note is a collection: every other page's plaintext
  stayed in memory, fully formed. Locking now empties every page and drops the
  collection.

- **The monitor kept scanning while locked.** The lock stopped the save and
  format timers but not the periodic host check, so a locked DuckNote went on
  probing the network on a timer, reading hosts out of the note still held in
  memory. The lock now stops the monitor, stops the idle timer, cancels a scan
  in flight, and empties the host table and the set-aside list. Unlocking puts
  them back from the vault.

- **Child windows stayed up.** The host card was closed, but a settings or link
  dialog open when the idle timer fired would sit on top of the veil. Every
  owned window is closed on lock.

- **The set-aside hosts were written in plaintext.** `IgnoredHosts` went into
  `settings.json` outside the container — a list of exactly the addresses you
  care about, next to a vault whose whole point is that nothing is written in
  clear. The vault has had an unused `Private` section since the port; the list
  goes there now when the container is open, and a plaintext list left by an
  earlier build is moved in and blanked on first run.

What the review found already correct: the password never leaves a
`SecureString` in the UI, the bytes derived from it are zeroed in `finally`, the
Argon2id output is zeroed, the key-encryption key is zeroed after `Unlock`, the
pending key is zeroed on both commit and dispose, and vault sections are zeroed
when replaced or cleared. The password fields are now also cleared when the gate
window closes by any path, not only on the paths that already did it.

### Changed

- **Every open and close of a branch animates, and the stagger adapts.** The
  animation was there and could not fire: `SyncNodes` keeps the `SideNode`
  instances already in the list and copies values onto them, while
  `Outline.Build` makes fresh ones each pass — so the node arriving at the toggle
  belonged to an older generation and its `Children` were not the objects in the
  list. `ContainerFromItem` returned null for every one of them and the cascade
  had nothing to animate. Rows are found by page and kind now, against the list
  itself, with no instance identity involved. The delay between rows is
  `min(28 ms, 190 ms / rows)`, so three sections cascade and thirty still finish
  in under a fifth of a second.

- **Faster scanning, and more hosts at once.** The ping attempts went out one
  after another, so a silent host cost `attempts × timeout` — two seconds each,
  which is what a /24 sweep spends most of its time on. They go out together
  now: one timeout, whatever the count. With that out of the way the host
  concurrency default rose from 64 to 256, which is only safe with a ceiling on
  sockets: 34 ports × 256 hosts would ask Windows for more ephemeral ports than
  it has. A budget shared by the whole scan now caps TCP connects in flight
  (default 900), and both numbers are in the settings, with the reason written
  next to them.

- **The app opens on the note, with the structure in the sidebar.** It opened on
  the host list. The saved `SidebarMode` was written on close and never read by
  anyone, so it is gone rather than left looking meaningful.

### Verified

Six new tests in the scan suite: the socket budget hands out exactly its
budget, makes the next taker wait, is not widened by a double release, gives up
on cancellation, and never exceeds itself under 200 concurrent takers; and the
ping attempts measured against the clock, which fails if they are sequential
again. 132 tests pass.

In the harness: a note of two pages with a password written in it, held by
reference from outside, must come back empty after the lock — blocks gone, text
gone, the word itself gone; the host table and the set-aside list empty; and the
single-instance guard refusing the second claim and allowing one again after
release.

For the caret: the offset measured before and after a repaint, on four kinds of
line — after an IPv4, after a domain name, with text following the address, and
on a line with no address at all, which must come through untouched. And with the
caret in the middle of a line rather than at its end. For the pruning: an address
removed from the note loses its row, writing it again brings it back, and a row
carrying a real result survives being pruned.

Thirty-two sabotages, all caught — but four were worthless until the tests or the
sabotages themselves were fixed. One was stopped by the compiler before the
harness ran. One produced invalid XAML, so the harness failed on a parse error
instead of on the assertion. The page-wiping check read `book.Pages`, which is
empty by then, so it passed whether or not the documents were cleared. And the
instance-guard sabotage opened the lock file without share-delete, which
`DeleteOnClose` refuses anyway — it was proving the wrong thing. A sabotage that
fails for the wrong reason is the same as a sabotage that was not caught.

### Earlier the same day: the tree animation and the host card

- **The tree would not animate on opening at all.** The first cause was the
  template: the sidebar recycles its containers, and a recycled container does
  not raise `Loaded` again, so the entrance trigger fired when the list was first
  built and never on an expand. Driving it from code uncovered the second cause,
  the stale nodes above. Both are gone, and the `Loaded` trigger with them — the
  whole list already animates when the sidebar switches mode, and leaving the
  trigger in place made the two fight over the same properties.

- **"Mostra dettagli" no longer drags you to the network view.** It switched tab
  before opening the card. The card is its own window beside the main one, so
  there was never a reason to leave the note you were reading.

An animation needs frames, and the harness renders none, so what it checks is
the mechanism the animation hangs from: the translate transform the code looks
up by name inside the row template, for a page row and for a section row. Rename
it and the glide silently stops happening — the sabotage proves the check
catches exactly that. Twenty-four sabotages, all caught.

The cascade itself, and the tab staying where it is, are a few lines each and
were read, not measured.

### Earlier: the sidebar, two notches

**Two notches on yesterday's sidebar.**

#### Changed

- **Two more pixels between a page's icon and its name.** The sheet sat 3 px from
  the title; it sits 5 px away now. The tree step went 26 → 28 with it, because
  the step has to equal the page toggle plus the icon or a section's title stops
  lining up with its page's — the alignment check measures that through the
  layout, so widening the gap alone would have failed it.

- **The accent bar on the left of a selected row is gone.** The selection is one
  surface: a soft accent fill and an accent-dark label. One state, one signal.
  Both sidebar modes lose it together — they share the template.

#### Verified

The harness now reads the gap between the icon's right edge and the label's left
edge through the rendered layout, and the bar's absence by name. Twenty-three
sabotages, all caught.

Two of them were not worth anything until rewritten. One still pointed at the
transform of the bar that had just been removed, so it could never apply. The
other injected the bar back as a sibling placed before `Grid.RenderTransform`,
which is invalid XAML: the harness failed on a parse error instead of on the
assertion — caught, but for the wrong reason, which is the same as not caught.
Wrapping the presenter and the bar in a Grid makes it valid, and the assertion
fails on its own merits.

### Earlier: the sidebar, redrawn

**The structure sidebar, redrawn.** A page now carries a page.

#### Added

- **Pages wear a sheet icon.** A small outlined page with a folded corner sits
  before every page name, and nothing sits before a heading: the two kinds of row
  are told apart at a glance instead of by reading their weight. The icon follows
  the row's state — grey normally, accent when the row is selected — and the page
  you are reading keeps it in the accent colour with a heavier stroke. That
  replaces the accent dot that used to sit at the far right of the row: a page's
  own icon saying "you are here" beats a dot at the other end of the line.

#### Changed

- **The selected row was three signals for one state.** A soft orange fill, an
  orange border *and* a bar that popped in at the left, all at once, on a row 18
  or 25 px tall — which is what made it look like a dialog from 2012. It is one
  surface now: a soft accent fill with a 7 px radius, **no border**, the label
  turning accent-dark, and a 2.5 px rounded bar that **grows into place** with a
  slight overshoot instead of appearing. The bar sits in the item's own gutter, 4
  px outside the fill, so it no longer collides with the tree lines of a heading.
  Hover stays what it was: a barely-there neutral fill.

- **One template for both sidebar modes.** The structure rows and the host rows
  had the same visuals written twice, which is how one of them ended up modern
  and the other dated. There is one `PickedRow` template now, and both styles
  point at it.

- **A section's title lines up with its page's.** The sheet icon takes 12 px in
  front of a page name, which would have pushed page titles right of their own
  sections — undoing the fix from the round before. The page toggle narrowed from
18 to 14 px so
  that toggle plus icon is exactly the tree step, 26: a page title and the titles
  under it now start at the same x, with the elbow pointing at the heading from 4
  px away.

#### Verified

Measured, not reasoned about this time. The harness builds the real
`MainWindow`, takes `BranchRow` and `BranchItem` out of its resources, puts a
page and a heading through a real ListBox in a shown window, and reads the
rendered tree: the page shows the sheet and the heading does not, the open page's
icon is the accent brush itself, the unselected row is transparent with no
border, the selected row's fill is the accent wash with the border still absent,
the label turns accent-dark, and the bar's transform starts at a quarter height
so it has somewhere to grow from.

**And the alignment is measured through the layout**, by transforming each
label's origin into the list's coordinates and comparing: page title and section
title must start within 0.6 px of each other. Narrowing the tree step back to 13
fails that check — which is what it is for, and what the previous release could
only claim.

Twenty-one sabotages, all caught. One did not apply on the first run: the
replacement text spanned two lines written with `\n` against a CRLF file, the
same mismatch that corrupted the sources in 0.0.9. Anchored on a single line it
applies, and the border coming back is caught twice.

### Earlier: six things that looked wrong

**Six things that looked wrong.** Not broken this time — misplaced, misshaped or
the wrong colour.

#### Added

- **Right-click an address and open its card.** *Mostra dettagli* joins *Analizza
  adesso* on an IP or a domain name in the note: it adds the host to the table if
  it is not there yet, switches to the network view, selects the row and opens the
  side panel on it. An address set aside says so instead, rather than opening an
  empty card.

#### Changed

- **A section's title now sits where its line points.** A heading left room for a
  fold toggle it can never have — only pages hold sections — so every title was
  pushed 15 px right of its own connector, with a void in between. The toggle's
  place is now kept only for pages, and the tree step widened from 13 to 26 so a
  section's text lands just past its parent's, not before it. The elbow stops 4 px
  short of the text, which is a tree line pointing at a label rather than a gap.

- **The network table has no horizontal scrollbar.** Four columns were a fixed
  width and the rest had minimums adding up to 790 px, so a narrow window grew a
  scrollbar. All but the status dot are proportional now, with minimums that fit
  in half the space, cells trim with an ellipsis, and horizontal scrolling is off.

- **The settings window is laid out, not stacked.** Eight cards of rows at four
  different label widths and five different margins became one grammar: a header,
  a card, rows of a fixed label and a fixed field, and **a divider between every
  row**. Papere moved out of *Aspetto* into their own card, security sits above
  the scan details because it is what people come for, and the probes are under
  one heading that says what they are for. Out went the *Motore parallelo di
  PowerShell 7* checkbox: forced on, disabled, unchangeable — a leftover of an era
  that ended two versions ago.

- **An address set aside reads as ordinary text.** It kept its colour and its
  monospaced font, which said "I am watching this" about something deliberately
  not watched. It now loses both the moment it is set aside — the note repaints
  under your eyes — and takes them back when the host does. A note opened with
  hosts already set aside never marks them at all.

- **The host card is coloured by state.** It was always the same dark orange.
  Green when the host answers, red when it does not, orange when it has never
  been checked, with a dot next to the title in the same tone. The tint is a 12%
  wash, not a fill, so the title and the subtitle keep the label colour and stay
  readable in both themes.

#### Verified

The harness grew three groups. **Set aside:** the address is recognised, green
because it answers, monospaced; set aside it keeps its text but takes the plain
colour and loses the monospace, with nothing left marked as a host; taken back it
is coloured again; and a document rendered from scratch while it is set aside
never marks it. **Tone:** every rank maps to the colour the request asked for,
every token exists in both palettes, and every wash is transparent enough
(alpha < 0x60) that text over it stays legible. **Windows:** the host card, the
link dialog and the settings window are each constructed and filled in both
themes — the settings window was rewritten whole, and a window that throws on
open is the one defect a build cannot catch.

Sixteen sabotages, all caught. Two of them were worthless on the first run: with
the set-aside predicate stubbed out the *compiler* stopped the build before the
harness ran, because an unread primary-constructor parameter is an error here.
Rewritten to read the parameter and ask it the wrong question, they fail the way
a real defect would — 3 and 1 red checks instead of none.

### Earlier: eight things that did not work

**Eight things that did not work.** Three of them were dead on arrival — menu
entries wired to nothing — and the worst one locked you out of your own note.

#### Fixed

- **The unlock button stopped responding after the second lock.** You could
  type the password and press Enter, but the button was dead to the mouse. Two
  flags are turned off while the vault is being opened — the form's hit-testing
  and the button's own enabled state — and the screen that shows the lock reset
  neither. After the first successful unlock they stayed off for good. The veil
  now has one place that puts it back on stage, and the lock screen goes through
  it instead of repeating half of it.

- **The checkboxes were not clickable.** Every click was thrown away by a guard
  asking for the character under the pointer with snapping switched off, which
  returns nothing even dead centre on the box — measured. With snapping on, the
  position arrives; whether it counts is now decided by where it falls inside
  the marker, not by which run it landed in, so it keeps working after an
  address splits the line into several runs. The box and the space left of it
  toggle; the text does not.

- **Pressing Enter left too much space.** A paragraph's default margin in WPF is
  `Auto`, not zero, and six of the eight places that make one took the default:
  the new line after Enter, the separator and the line after it, the lines a code
  block unfolds into, a fresh page, an empty document. Only the two that said
  `Margin = 0` were tight. There is now one factory, so the mistake has nowhere
  left to happen, and notes already carrying automatic margins are flattened the
  first time they open.

- **The host panel lagged behind the window while dragging.** Moving the window
  only restarted a 90 ms timer, so the panel did not follow — it jumped to the
  end of each pause. It now follows every move, and the timer keeps doing what it
  was for, which is letting the ducks settle. The panel also stops rewriting its
  own height on every move: that forced a layout pass and was the jerk itself.

- **"Non analizzare" did nothing, and never appeared on an address in the note.**
  No item of either context menu was connected to anything. A host set aside is
  now dropped from the table, kept out of future scans — single hosts, ranges and
  the monitor alike — and written into the settings so it survives a restart. The
  list's menu names the host it will set aside and lists the ones already aside,
  each clickable to take it back.

- **"Analizza adesso" appeared on any selected text and did nothing.** It now
  appears only when the right-click lands on an IP or a domain name — found by
  the same reader that colours addresses in the note, so the two can never
  disagree — and it adds that host to the table before scanning it, without
  clearing the rows already there. "Copia indirizzo" names the address too, and
  cut, copy, paste, paste-without-format and select-all, equally unwired, now
  work.

- **A link had nowhere to keep its address.** `[testo](indirizzo)` rendered as
  underlined text with the address in a tooltip, and a tooltip does not survive
  the note being saved — nor does `Tag` — so the address was lost on reopening.
  A real `Hyperlink` with its `NavigateUri` does survive, measured both ways. The
  link button now opens a window in the same dress as the others: the selected
  text on one side, the address to write or correct on the other, and a way to
  take the link off that leaves the text behind. Without a scheme, `https://` is
  added; an address that is not http, https or mailto is refused, in the dialog
  and again before anything is opened. Ctrl+click opens it.

- **The find bar opened and found nothing.** It was a full-width strip with two
  fields and no search behind them. It is now a small panel over the top right of
  the note, sliding down as it appears: type and it jumps to the first match and
  counts them, Enter and the arrows walk them, F3 and Shift+F3 too, Esc closes,
  Ctrl+F opens it on whatever is selected. Matches are found across runs, so a
  word half in bold is still one word. Replace moved behind a toggle, so the bar
  stays small when all you want is to find something.

#### Verified

A harness drives the real editor, the real scan session and the real commands
without opening a window. Margins: every one of the six paths that make a
paragraph, plus repairing a note that already has automatic ones and not
repairing it twice. Checkboxes: a hit on the box, a miss in the middle of the
line, a miss on the first letter of the text, and the state flipping back and
forth. Search: counting with and without regard for case, stepping forward and
wrapping round, stepping back from the first to the last, a word that is not
there, replacing one and replacing all, and a match spanning two runs. Links:
the address tidied, a bad scheme refused, a link made from a selection, read
back after a save with its text, found again under a real mouse click and when
the caret lands at its edge, edited, and taken off leaving the sentence whole.
Hosts: set aside and the row goes, set aside twice does nothing, taken back and
the row returns, the name matched whatever its case, and the ones read from the
settings staying out.

Each was checked against sabotaged code before being trusted: the automatic
margin put back gave 8 failures, the click target widened by one character 1,
case folded always 1, the search not wrapping 2, any scheme allowed 1, the link
not looked for around the position 1, the set-aside row left in place 4, and a
set-aside host let back into the queue 2.

The first run of that pass let the widened click target through: the test
clicked at the end of the line, far from the boundary it was supposed to defend.
The boundary is the first letter of the text, and that is what it clicks now.

Two fixes are not in the harness, because both live in a window that has to
exist: the unlock button and the panel following the drag. The first is a reset
of two flags in one method, the second one call moved out of a timer.

## [0.0.9] - 2026-10-07

**The note stops being its own source.** Markdown goes in and formatting comes
out; the note holds pages with names you choose; and the structure is a tree you
can fold.

It is where the tables already went in 0.0.8, and it works for the same reason:
the note is saved as XAML, so styles, borders and backgrounds survive being
closed and reopened. A paragraph's `Tag` does not — measured — so the look of a
line **is** its state, and nothing has to be kept in sync.

#### The markers are gone

Write `**così**` and you get **così**: the asterisks said how to read it and
their job is done. Up to 0.0.8 they stayed in the text, dimmed, and the note was
its own source. Now the document holds the formatting and Markdown is the way
in.

- **A code block is one block.** It was three paragraphs — a fence, the text, a
  fence. Now it is a single paragraph whose lines are separated by internal line
  breaks, shaded and monospaced, with no fence in sight. Enter inside it adds a
  line to the block instead of breaking it in two; the button pressed again
  dissolves it back into ordinary lines.
- **A separator is a line.** No dashes left at all — the cut is the paragraph's
  own border.
- **A bullet is a bullet** (`●`), a checkbox is a checkbox (`☐` / `☑`), a
  heading is just bigger. The markers that produced them are consumed.
- **The buttons set styles, not syntax.** Bold on three lines puts bold on three
  lines; there is no `**` that has to open and close inside a line, so nothing
  refuses a multi-line selection. Underline and strikethrough now coexist.
- **Enter knows where it is**: a line in a code block, the next item of a list,
  the end of the list on an empty item, an ordinary line after a heading.
- **`2 * 3 * 4` keeps its asterisks.** A marker with a space against it from the
  inside is not a marker — without that rule, turning markers into styles would
  eat arithmetic and never give it back.
- **Addresses are coloured without redrawing the line.** The old way rebuilt a
  paragraph from its text on every keystroke, which is exactly what would now
  destroy its formatting. Address runs are found and split in place, keeping
  whatever they were wearing: an address inside bold text stays bold.
- **Old notes convert themselves on first open**, once. The note as it was stays
  in `note.xaml.bak`, which saving always writes first, and tables are not
  flattened by the conversion.

#### Pages

One note was one document; now it is a collection of them.

- **A page is named by the `#` heading it opens with.** A page starting with
  `# Spesa` is called Spesa. The `+` button in the sidebar makes a page;
  right-click renames it, and the name is written into a field on its own row in
  the structure — a dialog for three words would be more work for whoever writes
  and whoever reads the code. From then on the name you typed wins, even if the
  heading changes under it.
- **Right-click removes a page**, except the last: a note without pages is not a
  note.
- **All pages live in one block of bytes**: a zip with one entry per page, and an
  index holding their order and their names, one line each. The vault keeps the
  note as a single blob, so the pages had to fit inside it — and a zip is a
  format that already exists, opens with any archive tool, and needed no parser
  of its own. An index line without a name still loads: that page simply has
  none.
- **A note from before becomes the first page**, rewritten in the new shape on
  first open.
- **Addresses are collected from every page**, not only the open one, so the host
  list and the network table cover the whole note.

#### The structure is a tree

Pages are the roots, and under each one are **its `##` sections** — nothing
else. The first level is already the page's name and does not repeat under
itself; the third is detail inside a section, and in the structure it would only
be noise. Two levels, read at a glance.

- **Lines show what leads where.** Not indentation, not one guide: proper tree
  lines. Each row draws its own piece — a vertical for every ancestor that still
  has siblings below, an elbow into its own entry, the tail under the elbow when
  someone follows, and the start of the trunk towards its children — and from
  one row to the next they meet. A page has no elbow, because it hangs from
  nothing, but it does lower the trunk towards its headings.
- **Opening and closing are animated.** A row that appears slides in from the
  left and fades up; closing a branch fades its rows out before they go. The
  transform lives inside the row's template, not in a setter on the style: a
  setter would hand one transform to every row, and animating one would animate
  all of them.
- **The toggles tell pages from headings.** A page's is filled and larger, in
  the accent colour; a heading's is a thin chevron. Opening a page and opening a
  heading are not the same act and should not look like it. Both are as wide as
  a finger now, not as wide as a character.
- **The rows are tighter.** Out went the coloured dot, which meant host state
  and nothing here, and the second line of small print under every page. A page
  row is 25 px and reads as a section title; a heading row is 18 and reads
  smaller but still reads.
- **The open page carries one accent dot**, and a count appears only when a
  search found the word in a page's body.
- **Pressing a page takes you back to the top of it**, onto its title, even when
  it was already the open one — which is the whole point of pressing it again.
  Pressing a row that is already selected raises no selection event, so the
  press itself is watched; the fold toggle and the rename field are left out of
  that, since pressing them does not mean wanting to go anywhere.
- **Searching looks everywhere.** The filter matches a page's name, its
  headings, and the text inside it. Searching is for finding, not for filtering
  names.

#### Also

- **The `H1`, `H2` and `H3` buttons scale like the headings they make** — 15,
  12.5 and 10.5 — and sit on one baseline. Centred one by one, three different
  sizes read as crooked; the toolbar button template now takes its content
  alignment from outside instead of hardcoding centre.
- **The sources carry no comments.** 544 lines came out of 21 files. What they
  explained — the WPF traps, the measurements behind each decision — is in this
  changelog, where it is readable by someone who is not already inside the code,
  and in the history of this file.

#### Fixed

- **A converted note was converted again at every open.** The check read the
  text alone, and on a numbered list the preview is `1. voce` — character for
  character the Markdown marker. It now also asks what the line already is: a
  line already dressed has no markers left to consume.

#### Verified

Measured end to end on a note written in the old format and opened by the real
executable, with `DUCKNOTE_HOME` pointed at a scratch folder:

| | before | after |
|---|---|---|
| markers in the visible text | `**`×2 `~~`×2 `==`×2 `` ` ``×8 `](`×1 `# `×2 | none |
| preview glyphs | none | ● ☐ ☑ |
| paragraphs with a background (code blocks) | 0 | 1 |
| internal line breaks | 0 | 1 |
| tables | 1 | 1 |
| note on disk | 4582 bytes of raw XAML | a 1571-byte archive |

Second open converts nothing. That second open is what found the re-conversion
bug — and the probe that found it also showed that three earlier runs had not
been failing at all: the first-start vault prompt is modal, and killing the
process while it waits means the note is never loaded. Worth writing down,
because it looked exactly like a broken save.

Three harnesses drive the rest without opening a window. **Formatting:** eleven
kinds of markup read back marker-free, every line kind identified by its look,
the XAML round-trip preserving text and look together, input rules applying on
close and leaving unclosed markers and arithmetic alone, all seven style buttons
and all six line buttons across a three-line selection, Enter in each of its
four situations, an address inside bold text keeping its weight. **Pages:**
names chosen, guessed and cleaned of control characters; three pages saved and
reread with their order, ids, names, text and formatting intact; a single old
note turned into one page; bytes that are neither, refused without taking
anything down; the last page refusing to be removed. **Tree:** a page named by
the `#` it opens with, only its `##` sections listed — a `###` and a second `#`
both absent, and no section ever holding children — the right rail width and row
height per kind, the toggle only where there are children and its place held
where there are none, pages and sections carrying different toggles, the open
page marked, folding a page taking what is shown from 5 rows to 3, the connector
tail present on a row with siblings below and absent on the last of a row, a
page without sections drawing no lines at all, and searching by section, by
name, by body text with its count, by nothing, and ignoring case.

Each harness was checked against sabotaged code before being trusted: markers
kept gave 8 failures, fence detection disabled 12, nesting flattened 3, the page
name dropped from the index 3, the connector tail removed 1, and every heading
level let back into the structure 8.

That last one passed at first, and the check was wrong rather than the code: it
looked for a vertical anywhere in the row and found the one going down to the
children. Made exact, it failed for a second reason — the harness formatted
`19,5` with a comma while the geometry is written `19.5` with a point, which is
exactly why the geometry is formatted culture-invariant and not with the
ambient culture.

Taking the comments out was held to the same standard. The tool reads C# through
Roslyn, so a `//` inside a string or an URL is not a comment, and it refuses to
write a file whose token sequence changed — removing a comment cannot change a
token. Its first run corrupted the sources: the cut mask was indexed on the
original text while the lines were counted on a copy with the line endings
normalised, so every CRLF shifted the cuts by one, cumulatively, and
`Models.SideNode` became `Models.SideNo`. The trap file had been LF-only. With
the normalisation done once and the guard in place, that same sabotage is
refused instead of written, the LF and CRLF copies of a trap file come out
identical, and over the whole repository the diff is 545 removals against a
single addition — a `#pragma` line that lost its trailing comment.

## [0.0.8] - 2026-10-07

#### Fixed

- **Live formatting stopped after the first line.** `FormatAll` walked the
  document with a lazy enumerator and redrew as it went — and redrawing a line
  changes the document, which kills the enumerator. The exception was caught by
  an empty `catch` wrapped around the whole loop, so everything past line one
  was left unformatted and nothing said so.

  This is why a table looked like rows of text: the command built the table and
  asked for a redraw that never happened. The list is now taken before anything
  is touched, and a line that refuses no longer stops the others.

- **Every formatting button now applies to the whole selection.** Bold, italic,
  underline, strikethrough, highlight, inline code and link used to refuse —
  *"select text inside a single line"*. Headings, quote, bullet, numbered and
  checkbox ignored the selection entirely and changed only the caret's line.
  Clear refused too.

  Markers wrap each line on its own: `**` does not cross a line ending, so a
  bold opened on one line and closed on another would not be bold. The line
  prefix stays outside the markers — `- **voce**`, not `**- voce**` — because a
  prefix inside markers stops being a prefix.

- **A prefix now replaces the one already there.** The strip pattern knew only
  `#` and `>`, so pressing bullet on a numbered line produced `- 1. testo`. One
  pattern now covers headings, quotes, bullets, numbers and checkboxes.

- **A table is now a table.** The buttons inserted three lines of pipe
  characters and left them as text: the note showed `| Colonna | Colonna |`,
  not a grid. They now build a real `Table` in the document — a header row with
  its own background and weight, one-pixel borders that never double up,
  padded cells.

  This was possible all along and nobody had used it: the note is saved as
  XAML, so a table survives being closed and reopened exactly like bold does.
  Two brushes for it, `TableLine` and `TableHeader`, had been sitting in
  `EditorBrushes` unused since the port.

#### Changed

- **A separator line looks like a separator.** It used to render as three grey
  minus signs. The cut is now drawn by the paragraph's own border, edge to
  edge, with the dashes still in the text — small and dim, like every other
  marker this note shows rather than hides.
- **A code block shades its body**, not just its two fence lines: inside the
  fence the text is monospaced on the code background, and no Markdown is
  looked for — a `*` in code is a `*`.
- **`[testo](indirizzo)` renders as a link.** The link button has always
  produced that shape and the view had no idea what it was, so it showed as
  plain text. The label now reads as a link and the address stays visible and
  dim.
- **List and number markers are dimmed** like every other marker. They were the
  only ones still the colour of the text.
- **The numbered list counts.** Pressing it on five lines gave five `1. `;
  they are now numbered 1 to 5. Blank lines inside a selection are left alone —
  a list has no item made of nothing.
- **The code fence surrounds the selection**, instead of taking the caret's
  line and pushing it inside a new block.
- **Rows and columns move around the caret**, not at the end of the block: the
  new row goes under the one you are in, the new column beside the one you are
  in, and what you remove is what you are in. Deleting the header is still
  refused — it and nothing else names the columns.
- **Markdown already written is recovered, not abandoned.** With the caret in a
  block of pipe lines, the table button converts that block instead of adding
  another table: cells keep their text, and the row of dashes does not become a
  row — in the text it separated the header from the body, and now the header
  does that itself.
- **A table cannot be born inside a table.** With the caret in a cell the
  button refuses and says so, rather than nesting a grid in a cell.

#### Verified

Two harnesses build the real editor, press the buttons and read the document
back.

**Tables:** a new table has two columns and two rows with its header shaded and
bordered, it survives the XAML round-trip with its text and its background,
add/remove row and column land on the right counts, deleting the header is
refused with a reason, and a three-line Markdown block becomes a three-row
table with `Host`/`Stato` as its header and the dashes gone.

**Formatting:** all seven marker buttons pressed on a three-line selection wrap
all three and unwrap them when pressed again; all five prefix buttons do the
same; a prefix replaces the one already there across five differently prefixed
lines; the numbered list counts 1, 2, 3; a selection dragged to the start of the
following line does not take it; after a command the selection is still on the
lines it touched, so the next button finds them; the code fence lands around the
selection and shades the body; the separator draws a border and loses it when
the line stops being a separator; and every style is read back off the document
— size, weight, slant, decorations, backgrounds, monospace, marker colour.

Both harnesses were checked against sabotaged code before being trusted: with
`Fresh()` building three columns the table harness failed on three counts, and
with `Change` touching only the first row the formatting harness failed on
thirty-five, naming each.

#### Known

`An_open_port_is_found_and_named` still fails intermittently when both test
projects run at once, and passes on its own — the socket flake documented in
0.0.4, untouched by this release.

## [0.0.7] - 2026-10-07

#### Added

- **Tables.** The five table buttons did nothing at all. They now insert a
  Markdown table and add or remove its rows and columns. Tables here are text,
  not objects, so adding a column means rewriting every row of the block — and
  knowing where the block starts and ends, which is wherever the unbroken run of
  lines beginning with a pipe does.

  Deleting the header row is refused: it and the dashes underneath are what hold
  the table together, and removing them would leave rows that are no longer
  anything.

- **Code block** and **horizontal rule**, which were also unwired. The fence
  goes on lines of its own — a code block does not open halfway through a line.

- **The find button** opens and closes the search bar. The fields in it still do
  not search; the button at least opens what it promises.

#### Changed

- **Scanning the note's hosts no longer jumps to the network view.** Whoever
  presses that button from the note wants the addresses there checked, not to be
  taken somewhere else: the colours change under their eyes, in the text. The
  network view still opens when the scan starts from there, where the result is
  the table.

#### Verified

Every button on the format bar was pressed in a harness and checked for an
actual change to the note — in the context where it makes sense: clearing wants
formatted text, redo wants something undone first, table commands want the caret
inside a table. All 25 do something. The one that does not is the header-row
delete, which refuses on purpose and says so in the status bar.

## [0.0.6] - 2026-10-05

#### Removed

- **The grid behind the note.** A tiled pattern of faint lines with an orange
  one every 160 px, drawn under the editor since the PowerShell days. It read as
  graph paper, which the note is not. The backdrop is now plain.

  The two brushes it used — `GridMinor` and `GridMajor` — went with it, from the
  window resources and from both palettes: a colour nothing paints with is a
  colour someone has to wonder about later.

## [0.0.5] - 2026-10-05

Three fixes on the animations, and the documentation the repository was missing.

#### Fixed

- **Picking a heading in Struttura now goes somewhere.** It did nothing at all:
  the guard that routes sidebar clicks asked for an address, and a heading has
  none, so it was dropped before it could do anything. It now scrolls the note
  to that line, travelling rather than jumping.
- **The ducks no longer stutter.** The first two attempts made it worse — tying
  the motion to the frame took jank from 0.4% to 14%, taking layout out of the
  loop made it worse still. Measuring the time *inside* the step showed it costs
  0.0 ms: it was never our code. Turning the ducks off on the same window told
  the real story (12.8% janky with them on, 0.0% off), and the cause was drawing
  twenty-two 512×512 rectangles with an opacity mask, shrunk to size by a
  transform — the compositor pays for the declared size, not the visible one.
  They are now born the size they are shown at: 1.1% jank.
- **The loading rings no longer crowd the line beneath them** on the key screen.
  Same defect already fixed under the lock veil: while working, rings and duck
  grow by half, but the scale is a render transform and layout cannot see it —
  the box was 132 tall while the circle took 168.

#### Added

- A `CHANGELOG.md`, which this repository did not have.
- The README now mentions what the application already did and the page did not
  say: scrolling to a heading, and the ducks behind the editor.

#### Known

The scanner tests touch real sockets and fail intermittently — on this code and
on 0.0.4 alike, verified by cloning and running them. Widening the timeouts did
not help: the loopback packet does not arrive late, sometimes it does not
arrive. Those changes were reverted rather than left in, because a test made
slower to fail is still a test that fails.

## [0.0.4] - 2026-09-26

**Rewritten in C# on .NET 10.** Up to 0.0.3 DuckNote was a single PowerShell
script, 10,143 lines of it. The interface is the same one — the XAML was carried
over unchanged — and containers written by 0.0.3 still open: a compatibility
suite opens fixtures generated by the old script on every build.

The script had reached the point where every new feature cost more than it was
worth: two scan engines to support both PowerShell 5.1 and 7, Argon2id written
in a language without unsigned 64-bit arithmetic, and nothing that could be
tested without opening the window.

#### Added

- **126 tests**, running headless in about a second. `DuckNote.Core` and
  `DuckNote.Scan` have no reference to WPF, which is what makes them possible.
- **Host card** docked to the side of the window, with everything a scan
  collected in sections. It follows the window, switches side when it runs out
  of room, and closes when the same host is clicked again.
- **Addresses from the note appear in the network table** as *mai controllato*,
  waiting, so the sidebar and the table say the same thing.
- **Picking a heading in Struttura scrolls the note to it**, travelling rather
  than jumping.
- **`DUCKNOTE_HOME`** moves the data folder, for keeping a note and its
  container on a USB stick next to the executable.
- **An installer** (Inno Setup), alongside the portable executable.
- **A wrong password is shown, not written**: the screen shakes, the field and
  its label flash red, the rings change colour and speed up, and what was typed
  erases itself. No dialog to dismiss.
- **The padlock is always in the toolbar**, open when the note has no key:
  whoever declined encryption at first start can still turn it on.

#### Changed

- One scan engine instead of two: `SemaphoreSlim`, `Channel` and
  `IAsyncEnumerable`.
- One self-contained executable instead of a launcher that extracted a script,
  put a BOM back and checked its hash.
- Auto-lock now defaults to **15 minutes**, was 30.
- Every surface shares one glass material, and the window has no light border.

#### Fixed

- **Closing the key screen no longer opens the note.** The cross and *Piu'
  tardi* used to mean the same thing; whoever gave up at the first screen got
  the note anyway.
- **Containers with impossible KDF costs are refused** instead of killing the
  open with an exception. The header is in the clear and nothing covers it with
  a MAC, so a tampered file could declare a terabyte of memory.
- **Passwords read from the input boxes are disposed.** `SecurePassword` hands
  out a fresh copy on every read; the strength meter was taking one per
  keystroke and leaving them all to the garbage collector.
- **The ducks no longer stutter.** They were drawn as 512×512 rectangles with a
  mask, shrunk to size by a transform — and the compositor pays for the declared
  size, not the visible one. Measured: 12.8% of frames janky with ducks on,
  0.0% with them off, 1.1% after the fix.
- The loading rings no longer overlap the line beneath them, on the key screen
  and under the lock veil.
- The lock veil closes the host card, and carries its own close and minimise
  buttons: it covers the title bar, so without them a locked window could not
  be closed.
- A scan that fails no longer takes the window down with it, and an unexpected
  error saves the note before leaving.

#### Known

The scanner tests touch real sockets and fail intermittently on a busy machine.
It is the test harness that is unreliable, not the scanner.

#### Not ported yet

Visible in the interface but without code behind them: table filter and search,
the status dropdown, CSV export, sending results into the note, find and
replace, tables in the editor, zoom, right-click menus.

---

Releases up to 0.0.3 belong to the PowerShell era and are not in this
repository's history.
