# DuckNote Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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
