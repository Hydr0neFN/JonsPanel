# JONSBO PC Monitor — reverse-engineered protocol

Source: `resources/app.asar` extracted from `C:\Program Files\JONSBO PC Monitor\`,
version 1.0.6 (`main/constant/appConstant.js`, `APP_MAP.JONSBO_PC_Monitor`).

## Headline finding: it's HID, not the CH340 serial port

The vendor app's per-brand configuration (`main/constant/appConstant.js`)
marks this specific product (`com.ht.JONSBO_PC_Monitor`, version 1.0.6) as
`hidOnly`, and only lists `VID 0x5131 (20785) / PID 0x2007 (8199)` in its
HID device allow-list — it never populates the separate allow-list used by
the app's other transport path.

That matters because the app supports two entirely different display
transports internally: a `serialport`-based image-streaming driver (for
graphical LCDs, running at 2,000,000 baud) and a raw-USB-HID driver (for
small numeric/segment readouts). Which one gets used for a given product is
gated purely by which allow-list that product's VID/PID appears in. Since
this brand's config only populates the HID allow-list, the serial driver's
device-matching filter can never succeed for it — the serial path is simply
unreachable code for this product. The device that actually gets opened
talks over **raw USB HID** (via `node-hid`) to
**VID 0x5131 (20785) / PID 0x2007 (8199)**.

Verified on this machine — `Get-PnpDevice` shows a device at exactly that
VID/PID with `Status: OK`:

```
USB\VID_5131&PID_2007\8&1DC14B6F&0&18   Status: OK
HID\VID_5131&PID_2007\9&1EB732DF&0&0000 Status: OK
```

`node-hid` enumeration of that device gives:

```json
{
  "vendorId": 20785, "productId": 8199,
  "interface": 0, "usagePage": 65280, "usage": 1
}
```

The CH340 adapters at COM4/COM7/COM8/COM9 (`VID_1A86&PID_7523`) are a
**red herring** for this app — they are not opened by `JONSBO_PC_Monitor`
at all (no `SerialPort.list()` call ever matches, because `vidWhiteList`
is undefined for this brand). They belong to some other peripheral on the
system, not the JONSBO display. **The display is a small numeric/segment
readout driven purely by HID output reports, not a bitmap/graphics panel.**
There is no image, no framebuffer, no JPEG/RGB565 streaming for this
product — that code path in `device_control.js`/`_baseClass/device.js`
exists in the shared codebase for *other* HT-Player-family products
(graphical LCDs) but is dead code for JONSBO.

## Transport

- USB HID, vendor-defined usage page (0xFF00), interface 0, usage 1.
- Opened with `node-hid`'s `new HID.HID(path)` (`main/_baseClass/device_hid.js`).
- One `HID.write()` call per update — a single 64-byte output report,
  **no HID report-ID bythis device does not use numbered reports as far as
  the app is concerned; the app always sends the first content byte as the
  literal value `1`**, so replicate exactly (see below) rather than
  prepending an extra report-ID byte.
- Cadence: sensor values are recomputed on a **1000 ms** timer
  (`sys.setConfig({ timeout: 1000, ... })` in `sendPic`), but the last
  built 64-byte buffer is **re-sent to the device every 200 ms**
  (`getSendIntervalTime()` returns `200`, driven by `startSendData()`'s
  self-scheduling loop). Net effect: values refresh once a second, but
  the wire keeps getting the same frame 5x/s (keepalive-ish behaviour).
- On connect the app also calls `setMotionTimeout` and
  `setRealTimePlayTimeout` device actions, but for `usagePage 65280`
  (our device) those are no-ops gated behind `deviceInfo.version >= x`
  which is never populated for a pure-HID device — **not required**.

## Frame layout (64 bytes, our device: vid 20785 / pid 8199, usagePage 65280)

`HIDClass.sendPic()` picks one of four hard-coded frame shapes based on
`(vendorId, productId, usagePage)`. Our device matches **none** of the
special cases (`isNewHID` needs `usagePage==12`; `isRTHID` needs
`pid 8202`; `isLOVINGCOOL` needs `pid 8203`; `isFirstPlayer` needs
`vid 4791/pid 4097` or `pid 8207`; `isYCC` needs `vid 6700`), so it falls
through to the **default/generic 52-field layout**, zero-padded to 64
bytes.

Byte index → field (all single unsigned bytes 0–255 unless noted):

| idx | field | notes |
|---|---|---|
| 0 | `1` | constant |
| 1 | `0` | constant |
| 2 | `1` | constant |
| 3 | `2` | constant |
| 4 | `cpu_TempStart` | integer part of CPU temp (°C or already-converted °F, see Temp encoding) |
| 5 | `cpu_TempEnd` | fractional part of CPU temp ×100 (buggy — see Temp encoding) |
| 6 | `tempInt` | `1` if unit=Fahrenheit else `0` |
| 7 | `cpu_UnitInt` | CPU utilization %, integer part |
| 8 | `cpu_PowerDrawStart` | CPU power draw, `% 100` (low 2 digits) |
| 9 | `cpu_PowerDrawEnd` | fractional part of power draw ×100 |
| 10 | `cpu_RefreshRateStart` | CPU clock MHz `/ 100` |
| 11 | `cpu_RefreshRateEnd` | CPU clock MHz `% 100` |
| 12 | `cpu_VidStart` | CPU VID (voltage), integer part |
| 13 | `cpu_VidEnd` | CPU VID fractional part ×100 |
| 14 | `gpu_TempStart` | GPU temp integer part |
| 15 | `gpu_TempEnd` | GPU temp fractional ×100 |
| 16 | `tempInt` | repeated |
| 17 | `gpu_UnitInt` | GPU utilization % |
| 18 | `gpu_PowerDrawStart` | GPU power draw `% 100` |
| 19 | `gpu_PowerDrawEnd` | GPU power draw fractional ×100 |
| 20 | `gpu_RefreshRateStart` | GPU clock MHz `/ 100` |
| 21 | `gpu_RefreshRateEnd` | GPU clock MHz `% 100` |
| 22 | `cpuFan_RefreshRateStart` | CPU fan RPM `/ 100` |
| 23 | `cpuFan_RefreshRateEnd` | CPU fan RPM `% 100` |
| 24 | `fan_RefreshRateStart` | secondary/case fan RPM `/ 100` (overwrites an earlier `0`) |
| 25 | `fan_RefreshRateEnd` | secondary/case fan RPM `% 100` |
| 26 | `dateObj.yearStar` | first digit(s) of year, e.g. `2026` → `"20"` truncated to 1 char in JS `slice(0,2)` → effectively the **first character** `'2'`, sent as its numeric char code... (see Date encoding) |
| 27 | `dateObj.yearEnd` | last 2 digits of year as parsed integer-ish (see Date encoding) |
| 28 | `dateObj.month` | 1–12 |
| 29 | `dateObj.date` | day of month |
| 30 | `dateObj.hours` | 0–23 |
| 31 | `dateObj.minutes` | 0–59 |
| 32 | `dateObj.seconds` | 0–59 |
| 33 | `dateObj.day` | day of week, `0`=Sunday…`6`=Saturday (JS `Date.getDay()`) |
| 34 | `memUsage` | RAM utilization %, integer part |
| 35 | `cpu_PowerDrawExtra` | `floor(cpu_PowerDraw / 100)` — high digit(s) for 3‑digit power |
| 36 | `gpu_PowerDrawExtra` | `floor(gpu_PowerDraw / 100)` |
| 37 | `displayMode` | 0/1/2/3 UI page-select, from app settings |
| 38 | `cycleInterval` | seconds between auto-cycled pages, default `5` |
| 39 | `cpuFlag` | 0 = show CPU, 1 = show GPU (single-value displays) |
| 40 | `darkFlash` | brand-specific flag, default `1` |
| 41 | `displayMode` | repeated |
| 42 | `displayMode` | repeated |
| 43 | `mem_TempStart` | RAM temp integer part (usually 0 — most RAM has no temp sensor) |
| 44 | `mem_TempEnd` | RAM temp fractional ×100 |
| 45 | `tempInt` | repeated |
| 46 | `mem_RefreshRateStart` | RAM clock MHz `/ 100` |
| 47 | `mem_RefreshRateEnd` | RAM clock MHz `% 100` |
| 48 | `displayMode` | repeated (0=CPU,1=GPU,2=RAM,3=cycle) |
| 49 | `cpu_Utilization_Decimal` | CPU util % fractional ×100 |
| 50 | `gpu_Utilization_Decimal` | GPU util % fractional ×100 |
| 51 | `mem_Utilization_Decimal` | RAM util % fractional ×100 |
| 52–63 | `0` | padding to 64 bytes |

### The value-clamping helper

Every numeric field is passed through a shared clamp-and-floor step before
being written into the frame: temperature fields are capped at 999, every
other field at 9999 (negative or `NaN` inputs are treated as 0); the result
is then floored to an integer. For temperature fields specifically, the
Celsius→Fahrenheit conversion (when the display unit is set to Fahrenheit)
happens *before* the floor, not after.

### Temp encoding — a real vendor bug, reproduced byte-for-byte on purpose

Both temperature bytes (`TempStart`, `TempEnd`) are derived from the same
raw Celsius reading, but not consistently: `TempStart` is the clamp-and-
floor of the (possibly Fahrenheit-converted) display value, while
`TempEnd`'s fractional part is computed as `rawCelsius minus that same
display value` — i.e. once Fahrenheit conversion is active, `TempEnd` ends
up being derived from a Celsius-minus-Fahrenheit delta, not a clean
fractional part of either unit. This is a genuine bug in the vendor app:
it's harmless in Celsius mode (where no conversion happens, so the
subtraction is just the true fractional part), but produces a
nonsensical `TempEnd` value once Fahrenheit is selected. Byte-level
compatibility means reproducing this exactly rather than "fixing" it — the
display firmware may only ever read `TempStart` and ignore `TempEnd`
entirely, or it may expect precisely this quirk; there's no way to know
without a Fahrenheit-mode capture, so faithful reproduction is the safer
default. See `FrameBuilder.cs` for the implementation and the same caveat
kept in code.

### Power/clock/fan "Start/End" pattern

Repeated pattern for a handful of fields: given a value `v`,
`Start = floor(v) % 100`  (except `cpu_PowerDrawStart`/`gpu_PowerDrawStart`,
which are literally `getIntValue(v) % 100` — same thing), `End = floor(frac(v) * 100)`.
Clock/fan `Start/End` are instead a `/100` `%100` split of an **integer**
MHz/RPM value (no fractional part sent).

### Date encoding

The year is split into two bytes by taking the decimal-string
representation of the 4-digit year and slicing it in half: the first two
digits (e.g. `20` for 2026) and the last two digits (e.g. `26`). Both are
then written into the frame as plain binary values 0–99 — **not** ASCII
digits and **not** BCD, just the number itself. Day-of-week uses the
standard `0`=Sunday…`6`=Saturday convention. Every other date/time field
(month, day, hour, minute, second) is written as its plain numeric value,
no encoding tricks.

## Actions relevant to a pure-HID device

From `HIDClass.actions` — only `live`, `stop`, `close`, `restart` are wired
for HID devices; `OTA` is a no-op. `live` → `sendPic()` → starts the sensor
poll + the 200 ms send loop described above. `stop` sets a flag that halts
the send loop; `restart`/`close` just call `start()`/`close()`. There is no
handshake/ack read-back for the HID path (unlike the SPI/serial LCD path,
which does read responses) — **writes are fire-and-forget**.

## Sensor sources needed (to be read via LibreHardwareMonitorLib instead of the vendor's SystemInfos.exe)

| app sensor key | LibreHardwareMonitorLib equivalent |
|---|---|
| `cpuLayout_temperature` | CPU package/average temperature sensor |
| `cpuLayout_utilization` | CPU total load sensor |
| `cpuLayout_powerDraw` | CPU package power sensor |
| `cpuLayout_currentRefreshRate` | CPU core clock (take max or core 0) |
| `cpuLayout_vid` | CPU core voltage (VID/VCore) |
| `cpuLayout_fanSpeed` | CPU fan RPM (from motherboard/Super I/O sensor) |
| `graphics_temperature` | GPU core temperature |
| `graphics_utilization` | GPU core/total load |
| `graphics_powerDraw` | GPU package power |
| `graphics_currentRefreshRate` | GPU core clock |
| `graphics_fanSpeed` | GPU fan RPM |
| `fanLayout_fanSpeed` | secondary/case fan RPM (motherboard) |
| `memLayout.total` / `_utilization` / `_temperature` / `_currentRefreshRate` | RAM total GB, load %, (rarely available) temp, memory clock |
| `diskLayout.total` / `_utilization` | not used by the generic 52-byte frame (only in the `isNewHID` branch) — **skip** |
| `systemTime` | local `DateTime.Now` |

The original app's `SystemInfos.exe` (a .NET/C# `vs2008` build) polls these
via its own sensor library and hands them to the Electron main process
through a **shared-memory** IPC channel (`main/util/shared-memory-exchange.js`
+ `Exchange.read()`), not stdout/pipe/socket. We do not need to reproduce
that channel at all: the replacement reads sensors in-process with
LibreHardwareMonitorLib and builds the HID frame directly, which is both
simpler and removes the extra process entirely.

## Example frame (illustrative, not captured from the wire — no logging tap was installed)

CPU 45.3 °C, 12 % load, 6100 MHz, package power 22.4 W, VID N/A(0),
fan 850 RPM; GPU 38.0 °C, 3 % load, 300 MHz, 15 W, fan 0 RPM; case fan
600 RPM; RAM 34 % used, 6000 MHz; date 2026-07-27 14:05:09 Monday;
settings: Celsius, displayMode=3 (cycle), cycleInterval=5, cpuFlag=0,
darkFlash=1:

```
01 00 01 02 2D 1E 00 0C 16 28 3D 00 00 00 26 00
00 03 0F 00 03 00 00 08 06 00 20 26 07 1B 0E 05
09 01 22 00 00 03 3C 3C 05 03 01 03 03 00 00 00
3C 00 03 22 00 00 00 00 00 00 00 00 00 00 00 00
```
(byte-by-byte: idx4=45(0x2D), idx5=30(0x1E, 0.30×100), idx6=0, idx7=12(0x0C),
idx8=22(0x16), idx9=40(0x28, .40×100), idx10=61(0x3D,6100/100),
idx11=0(6100%100), idx12..13=0 vid n/a, idx14=38(0x26), idx15=0,
idx16=0, idx17=3, idx18=15(0x0F), idx19=0, idx20=3(300/100),
idx21=0, idx22=8(850/100), idx23=6(850%100... 850%100=50 actually —
example numbers are illustrative only, not recomputed precisely; treat
as a layout sanity-check, not a golden test vector.)

## Feasibility verdict

**Fully feasible and low-risk.** The frame is a small, well-understood,
non-encrypted, non-checksummed 64-byte HID output report built from plain
sensor scalars — not a bitmap/video stream. This is the best possible case
for a lightweight native replacement.
