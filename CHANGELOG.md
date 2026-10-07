# DuckNote Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.0.9] - 2026-10-07

**The note stops being its own source.** Markdown goes in and formatting comes
out; the note holds pages with names you choose; and the structure is a tree you
can fold.

It is where the tables already went in 0.0.8, and it works for the same reason:
the note is saved as XAML, so styles, borders and backgrounds survive being
closed and reopened. A paragraph's `Tag` does not — measured — so the look of a
line **is** its state, and nothing has to be kept in sync.

### The markers are gone

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

### Pages

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

### The structure is a tree

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

### Also

- **The `H1`, `H2` and `H3` buttons scale like the headings they make** — 15,
  12.5 and 10.5 — and sit on one baseline. Centred one by one, three different
  sizes read as crooked; the toolbar button template now takes its content
  alignment from outside instead of hardcoding centre.

### Fixed

- **A converted note was converted again at every open.** The check read the
  text alone, and on a numbered list the preview is `1. voce` — character for
  character the Markdown marker. It now also asks what the line already is: a
  line already dressed has no markers left to consume.

### Verified

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

## [0.0.8] - 2026-10-07

### Fixed

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

### Changed

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

### Verified

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

### Known

`An_open_port_is_found_and_named` still fails intermittently when both test
projects run at once, and passes on its own — the socket flake documented in
0.0.4, untouched by this release.

## [0.0.7] - 2026-10-07

### Added

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

### Changed

- **Scanning the note's hosts no longer jumps to the network view.** Whoever
  presses that button from the note wants the addresses there checked, not to be
  taken somewhere else: the colours change under their eyes, in the text. The
  network view still opens when the scan starts from there, where the result is
  the table.

### Verified

Every button on the format bar was pressed in a harness and checked for an
actual change to the note — in the context where it makes sense: clearing wants
formatted text, redo wants something undone first, table commands want the caret
inside a table. All 25 do something. The one that does not is the header-row
delete, which refuses on purpose and says so in the status bar.

## [0.0.6] - 2026-10-05

### Removed

- **The grid behind the note.** A tiled pattern of faint lines with an orange
  one every 160 px, drawn under the editor since the PowerShell days. It read as
  graph paper, which the note is not. The backdrop is now plain.

  The two brushes it used — `GridMinor` and `GridMajor` — went with it, from the
  window resources and from both palettes: a colour nothing paints with is a
  colour someone has to wonder about later.

## [0.0.5] - 2026-10-05

Three fixes on the animations, and the documentation the repository was missing.

### Fixed

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

### Added

- A `CHANGELOG.md`, which this repository did not have.
- The README now mentions what the application already did and the page did not
  say: scrolling to a heading, and the ducks behind the editor.

### Known

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

### Added

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

### Changed

- One scan engine instead of two: `SemaphoreSlim`, `Channel` and
  `IAsyncEnumerable`.
- One self-contained executable instead of a launcher that extracted a script,
  put a BOM back and checked its hash.
- Auto-lock now defaults to **15 minutes**, was 30.
- Every surface shares one glass material, and the window has no light border.

### Fixed

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

### Known

The scanner tests touch real sockets and fail intermittently on a busy machine.
It is the test harness that is unreliable, not the scanner.

### Not ported yet

Visible in the interface but without code behind them: table filter and search,
the status dropdown, CSV export, sending results into the note, find and
replace, tables in the editor, zoom, right-click menus.

---

Releases up to 0.0.3 belong to the PowerShell era and are not in this
repository's history.
