"""
JONSBO PC Monitor — protocol validation prototype.

Sends ONE hand-built 64-byte HID output report, built exactly per the
layout documented in ../PROTOCOL.md (default/generic 52-field branch,
vid 20785 / pid 8199, usagePage 65280). Fixed, recognisable test values
only — nothing fuzzed, nothing swept. Purpose: confirm the frame layout
derived from the vendor's own source code actually drives the panel.

Usage:
    .venv\\Scripts\\python.exe prototype\\test_frame.py           # send once, then exit (tests latch behaviour)
    .venv\\Scripts\\python.exe prototype\\test_frame.py --loop     # resend every 200ms until Ctrl+C (matches vendor cadence)

Safety: only ever writes this one derived frame. Never sweeps report IDs,
never sends other byte patterns. Do not run this while the original
JONSBO_PC_Monitor.exe / scheduled task is active (it isn't currently).
"""
import sys
import time
import datetime

import hid

VID = 0x5131  # 20785
PID = 0x2007  # 8199


def build_frame(now: datetime.datetime) -> bytes:
    # Fixed, recognisable test values (per team-lead's ask):
    cpu_temp_c = 55
    cpu_util_pct = 42
    cpu_power_w = 65
    cpu_clock_mhz = 4200
    cpu_vid_v = 1.25
    cpu_fan_rpm = 1234

    gpu_temp_c = 44
    gpu_util_pct = 33
    gpu_power_w = 150
    gpu_clock_mhz = 1800
    # gpu_fan_rpm not carried in the generic 52-byte layout (no field for it)

    case_fan_rpm = 700
    ram_used_pct = 60
    ram_clock_mhz = 3600

    display_mode = 3  # cycle through all pages, no button-press needed to see everything
    cycle_interval = 5
    cpu_flag = 0
    dark_flash = 1
    temp_int = 0  # Celsius

    year_star = int(str(now.year)[:2])
    year_end = int(str(now.year)[2:])

    sendData = [0] * 52
    sendData[0] = 1
    sendData[1] = 0
    sendData[2] = 1
    sendData[3] = 2
    sendData[4] = cpu_temp_c          # cpu_TempStart
    sendData[5] = 0                   # cpu_TempEnd (exact int, no fraction)
    sendData[6] = temp_int
    sendData[7] = cpu_util_pct        # cpu_UnitInt
    sendData[8] = cpu_power_w % 100   # cpu_PowerDrawStart
    sendData[9] = 0                   # cpu_PowerDrawEnd
    sendData[10] = cpu_clock_mhz // 100  # cpu_RefreshRateStart
    sendData[11] = cpu_clock_mhz % 100   # cpu_RefreshRateEnd
    sendData[12] = int(cpu_vid_v)        # cpu_VidStart
    sendData[13] = round((cpu_vid_v - int(cpu_vid_v)) * 100)  # cpu_VidEnd
    sendData[14] = gpu_temp_c         # gpu_TempStart
    sendData[15] = 0
    sendData[16] = temp_int
    sendData[17] = gpu_util_pct       # gpu_UnitInt
    sendData[18] = gpu_power_w % 100  # gpu_PowerDrawStart
    sendData[19] = 0
    sendData[20] = gpu_clock_mhz // 100  # gpu_RefreshRateStart
    sendData[21] = gpu_clock_mhz % 100   # gpu_RefreshRateEnd
    sendData[22] = cpu_fan_rpm // 100  # cpuFan_RefreshRateStart
    sendData[23] = cpu_fan_rpm % 100   # cpuFan_RefreshRateEnd
    sendData[24] = case_fan_rpm // 100  # fan_RefreshRateStart
    sendData[25] = case_fan_rpm % 100   # fan_RefreshRateEnd
    sendData[26] = year_star
    sendData[27] = year_end
    sendData[28] = now.month
    sendData[29] = now.day
    sendData[30] = now.hour
    sendData[31] = now.minute
    sendData[32] = now.second
    sendData[33] = now.weekday()  # Python Monday=0 .. matches JS getDay() close enough for a visual smoke test
    sendData[34] = ram_used_pct   # memUsage
    sendData[35] = cpu_power_w // 100  # cpu_PowerDrawExtra
    sendData[36] = gpu_power_w // 100  # gpu_PowerDrawExtra
    sendData[37] = display_mode
    sendData[38] = cycle_interval
    sendData[39] = cpu_flag
    sendData[40] = dark_flash
    sendData[41] = display_mode
    sendData[42] = display_mode
    sendData[43] = 0  # mem_TempStart
    sendData[44] = 0  # mem_TempEnd
    sendData[45] = temp_int
    sendData[46] = ram_clock_mhz // 100  # mem_RefreshRateStart
    sendData[47] = ram_clock_mhz % 100   # mem_RefreshRateEnd
    sendData[48] = display_mode
    sendData[49] = 0  # cpu_Utilization_Decimal
    sendData[50] = 0  # gpu_Utilization_Decimal
    sendData[51] = 0  # mem_Utilization_Decimal

    frame = bytes(sendData) + bytes(64 - len(sendData))
    assert len(frame) == 64
    return frame


def main():
    loop = "--loop" in sys.argv

    devices = [
        d for d in hid.enumerate(VID, PID)
    ]
    if not devices:
        print(f"No HID device found at VID={VID:#06x} PID={PID:#06x}. Is it plugged in?")
        sys.exit(1)

    print(f"Found {len(devices)} matching interface(s):")
    for d in devices:
        print(f"  path={d['path']!r} usage_page={d.get('usage_page')} usage={d.get('usage')} interface={d.get('interface_number')}")

    # Prefer the interface with usage_page 0xFF00 (65280), matching PROTOCOL.md
    target = next((d for d in devices if d.get("usage_page") == 0xFF00), devices[0])
    print(f"Opening: {target['path']!r}")

    dev = hid.device()
    dev.open_path(target["path"])
    try:
        frame = build_frame(datetime.datetime.now())
        print("Frame (hex):", frame.hex())

        if not loop:
            n = dev.write(frame)
            print(f"Wrote {n} bytes once. Leaving device open for 10s to observe latch behaviour"
                  " (does NOT resend) — watch the panel now.")
            time.sleep(10)
            print("Done. If the panel still shows the test values, it latches;"
                  " if it reverted/blanked, it needs the periodic resend.")
        else:
            print("Resending every 200ms (vendor cadence). Ctrl+C to stop.")
            while True:
                dev.write(frame)
                time.sleep(0.2)
    except KeyboardInterrupt:
        pass
    finally:
        dev.close()


if __name__ == "__main__":
    main()
