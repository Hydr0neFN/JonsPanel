# Contact

**Email:** ruanyouyi@hotmail.com

## Rights holders

If you are a rights holder and believe anything in this repository
infringes your rights, please email the address above. I will respond
promptly and, if the concern is well founded, act on it.

I would much rather hear from you directly than have this go through a
platform takedown process. A message to the address above will reach me.

For the record, and stated in good faith:

- This project contains **no** JONSBO source code, binaries, firmware,
  fonts, icons, or other assets. The vendor application was examined
  locally to determine how to talk to a USB HID device I own; nothing
  from it is redistributed here.
- The protocol notes in [PROTOCOL.md](PROTOCOL.md) describe factual
  properties of a hardware interface — byte offsets, value encodings,
  update cadence. They are interoperability documentation, not a copy of
  anyone's source.
- The implementation in `src/` is my own. Where it reproduces the
  vendor's behaviour exactly — including a unit-conversion quirk that
  looks like a bug — that is because the panel firmware expects those
  bytes, not because the code was transliterated.
- "JONSBO" is used only descriptively, to say which hardware this works
  with. This project is not affiliated with, endorsed by, or connected to
  JONSBO.

## Bugs and other hardware

Open an issue. If you are trying this on a board or panel other than the
one listed under "Tested hardware" in the [README](README.md), please
include the output of `JonsboMonitor.exe --dump-sensors` — sensor names
vary by motherboard and Super I/O chip, and that output is what makes it
possible to map them.
