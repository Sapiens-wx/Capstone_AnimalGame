"""Offline FMOD bank checks. Writes recordings/reports only under Library."""
import ctypes as c
import hashlib
import json
import math
import struct
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

import numpy as np

REPO = Path(__file__).resolve().parents[4]
PROJECT = Path(r"C:\Users\PC\FMODProjects\Capstone_AnimalGame")
BANKS = PROJECT / "Build/Desktop"
OUTPUT = REPO / "Library/FMODValidation/OfflineAudio"
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE = REPO / "FMOD/SourceAudio/Milestone1"
CHARGE_LOOP_START = .71
CHARGE_LOOP_END = .85
CHARGE_LOOP_PERIOD = CHARGE_LOOP_END - CHARGE_LOOP_START


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def samples(path):
    data = path.read_bytes()
    assert data[:4] == b"RIFF" and data[8:12] == b"WAVE", path
    cursor, fmt, payload = 12, None, None
    while cursor + 8 <= len(data):
        key, size = struct.unpack_from("<4sI", data, cursor)
        value = data[cursor + 8:cursor + 8 + size]
        if key == b"fmt ":
            fmt = value
        if key == b"data":
            payload = value
        cursor += 8 + size + size % 2
    kind, channels, rate, _, _, bits = struct.unpack_from("<HHIIHH", fmt)
    if kind == 65534:
        kind = struct.unpack_from("<H", fmt, 24)[0]
    if kind == 3:
        signal = np.frombuffer(payload, dtype="<f4").astype(float)
    elif bits == 24:
        raw = np.frombuffer(payload, np.uint8).reshape(-1, 3).astype(np.int32)
        signal = ((raw[:, 0] | raw[:, 1] << 8 | raw[:, 2] << 16) << 8 >> 8) / 8388608.0
    else:
        assert kind == 1 and bits in (16, 32), (kind, bits)
        signal = np.frombuffer(payload, dtype="<i" + str(bits // 8)).astype(float) / 2 ** (bits - 1)
    return signal.reshape(-1, channels), rate


def levels(signal, rate):
    tail = signal[-max(1, round(rate * .02)):]
    return dict(seconds=len(signal) / rate, peak=float(np.max(np.abs(signal))),
                rms=float(np.sqrt(np.mean(signal * signal))),
                pcm16_limit_samples=int(np.count_nonzero(np.abs(signal) >= 32767 / 32768)),
                last_sample_peak=float(np.max(np.abs(signal[-1]))),
                last_20ms_rms=float(np.sqrt(np.mean(tail * tail))))


lib = c.WinDLL(str(REPO / "Assets/Plugins/FMOD/platforms/win/lib/x86_64/fmodstudio.dll"))
P, I, U = c.c_void_p, c.c_int, c.c_uint


def bind(name, args):
    fn = getattr(lib, name)
    fn.argtypes, fn.restype = args, I

    def checked(*values):
        result = fn(*values)
        if result:
            raise RuntimeError(f"{name}: FMOD_RESULT={result}")
    return checked


create = bind("FMOD_Studio_System_Create", [c.POINTER(P), U])
core = bind("FMOD_Studio_System_GetCoreSystem", [P, c.POINTER(P)])
set_output = bind("FMOD5_System_SetOutput", [P, I])
set_format = bind("FMOD5_System_SetSoftwareFormat", [P, I, I, I])
set_buffer = bind("FMOD5_System_SetDSPBufferSize", [P, U, I])
get_channels = bind("FMOD5_System_GetChannelsPlaying", [P, c.POINTER(I), c.POINTER(I)])
initialize = bind("FMOD_Studio_System_Initialize", [P, I, U, U, P])
load_bank = bind("FMOD_Studio_System_LoadBankFile", [P, c.c_char_p, U, c.POINTER(P)])
load_samples = bind("FMOD_Studio_Bank_LoadSampleData", [P])
flush_samples = bind("FMOD_Studio_System_FlushSampleLoading", [P])
get_event = bind("FMOD_Studio_System_GetEvent", [P, c.c_char_p, c.POINTER(P)])
is_3d = bind("FMOD_Studio_EventDescription_Is3D", [P, c.POINTER(I)])
is_one_shot = bind("FMOD_Studio_EventDescription_IsOneshot", [P, c.POINTER(I)])
instance = bind("FMOD_Studio_EventDescription_CreateInstance", [P, c.POINTER(P)])
start = bind("FMOD_Studio_EventInstance_Start", [P])
stop = bind("FMOD_Studio_EventInstance_Stop", [P, I])
set_volume = bind("FMOD_Studio_EventInstance_SetVolume", [P, c.c_float])
release_instance = bind("FMOD_Studio_EventInstance_Release", [P])
timeline = bind("FMOD_Studio_EventInstance_GetTimelinePosition", [P, c.POINTER(I)])
update = bind("FMOD_Studio_System_Update", [P])
release = bind("FMOD_Studio_System_Release", [P])


def render(event_path, seconds, volume=1.0, count=1, suffix="", interval=0):
    output_file = OUTPUT / (event_path.rsplit("/", 1)[1] + suffix + ".wav")
    studio, mixer = P(), P()
    create(c.byref(studio), 0x00020314)
    positions, real_channels = [], []
    try:
        core(studio, c.byref(mixer))
        set_output(mixer, 5)  # FMOD_OUTPUTTYPE_WAVWRITER_NRT
        set_format(mixer, 48000, 3, 0)  # Stereo
        set_buffer(mixer, 256, 4)
        output_name = c.create_string_buffer(str(output_file).encode())
        initialize(studio, 64, 4, 3, c.cast(output_name, P))
        for filename in ("Master.bank", "Master.strings.bank", "RobotTools.bank"):
            bank = P()
            load_bank(studio, str(BANKS / filename).encode(), 0, c.byref(bank))
            load_samples(bank)
        flush_samples(studio)
        description, playing, spatial, one_shot = P(), P(), I(), I()
        get_event(studio, event_path.encode(), c.byref(description))
        is_3d(description, c.byref(spatial))
        is_one_shot(description, c.byref(one_shot))
        assert not spatial.value, event_path
        playing_instances = []
        scheduled_starts = {}
        for index in range(count):
            playing = P()
            instance(description, c.byref(playing))
            set_volume(playing, volume)
            playing_instances.append(playing)
            frame = round(index * interval * 48000 / 256)
            scheduled_starts.setdefault(frame, []).append(playing)
        for frame in range(math.ceil(seconds * 48000 / 256)):
            for scheduled in scheduled_starts.get(frame, []):
                start(scheduled)
            update(studio)
            position = I()
            timeline(playing, c.byref(position))
            positions.append(position.value)
            total, real = I(), I()
            get_channels(mixer, c.byref(total), c.byref(real))
            real_channels.append(real.value)
        for playing in playing_instances:
            stop(playing, 1)
            release_instance(playing)
        for _ in range(40):
            update(studio)
    finally:
        release(studio)
    signal, rate = samples(output_file)
    steady = signal[rate:3 * rate]
    derivative = np.max(np.abs(np.diff(steady, axis=0)), axis=1)
    top = np.argsort(derivative)[-20:]
    # FMOD can report a small backwards correction on transition entry as well
    # as the large jump to the loop destination. Count only the latter.
    wraps = [i for i in range(1, len(positions)) if positions[i - 1] - positions[i] > 50]
    # Ignore the first traversal, which includes the non-looping charge attack.
    wrap_periods = np.diff(wraps[-10:]) * 256 / 48000
    period_samples = round(CHARGE_LOOP_PERIOD * rate)
    repeated = signal[rate + period_samples:3 * rate + period_samples]
    period_error = float(np.sqrt(np.mean((steady - repeated) ** 2)))
    expected_period_is_best = all(
        period_error <= float(np.sqrt(np.mean((steady - signal[rate + shift:3 * rate + shift]) ** 2)))
        for shift in range(period_samples - 48, period_samples + 49)
    )
    return dict(event=event_path, instance_volume=volume, instance_count=count, interval_seconds=interval,
                is_3d=bool(spatial.value), one_shot=bool(one_shot.value),
                timeline_min=min(positions), timeline_max=max(positions),
                last_second_timeline_min=min(positions[-188:]), last_second_timeline_max=max(positions[-188:]),
                steady_derivative_p99=float(np.percentile(derivative, 99)),
                steady_derivative_max=float(np.max(derivative)),
                last_second_real_channels_min=min(real_channels[-188:]),
                last_second_real_channels_max=max(real_channels[-188:]),
                wrap_periods_seconds=wrap_periods.tolist(),
                steady_rms=float(np.sqrt(np.mean(steady * steady))),
                repeat_loop_rms_error=period_error,
                repeat_loop_is_best_local_match=expected_period_is_best,
                largest_derivative_times=sorted([(int(i) + rate) / rate for i in top]),
                output=str(output_file), **levels(signal, rate))


before = {str(p): digest(p) for p in SOURCE.rglob("*.wav")}
metadata = [obj for p in (PROJECT / "Metadata").rglob("*.xml") for obj in ET.parse(p).getroot()]
classes = Counter(obj.attrib.get("class") for obj in metadata)
assert classes["Event"] == 7 and classes["AudioFile"] == 7, classes
for forbidden in ("Snapshot", "MixerVCA", "MixerGroup", "MixerReturn", "Plugin", "SpatialiserEffect", "ObjectSpatialiserEffect"):
    assert classes[forbidden] == 0, (forbidden, classes[forbidden])
master = next(obj for obj in metadata if obj.attrib.get("class") == "MixerMaster")
master_volume = master.find("property[@name='volume']/value")
report = {"metadata_classes": dict(classes), "master_db": float(master_volume.text) if master_volume is not None else 0,
          "source": {}, "renders": []}
for path in SOURCE.rglob("*.wav"):
    signal, rate = samples(path)
    info = levels(signal, rate)
    if path.name == "Scan_Charge.wav":
        a, b = round(CHARGE_LOOP_START * rate), round(CHARGE_LOOP_END * rate)
        region = signal[a:b]
        info["loop_join_jump"] = float(np.max(np.abs(signal[a] - signal[b - 1])))
        info["loop_rms"] = float(np.sqrt(np.mean(region * region)))
        info["loop_adjacent_sample_delta_p99"] = float(np.percentile(np.abs(np.diff(region, axis=0)), 99))
    report["source"][path.name] = info
for event_path in ("event:/Robot/Scan/Charge", "event:/Robot/Scan/Pulse", "event:/Robot/Camera/Move"):
    report["renders"].append(render(event_path, 4))
report["renders"].append(render("event:/Robot/Scan/Pulse", 3, count=2, suffix="Double"))
report["renders"].append(render("event:/Robot/Scan/Pulse", 4, count=5, suffix="Five300ms", interval=.3))
assert before == {str(p): digest(p) for p in SOURCE.rglob("*.wav")}, "Source WAV changed during verification"
report["source_sha256"] = before
(OUTPUT / "AudioValidation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
for result in report["renders"]:
    assert result["pcm16_limit_samples"] == 0, ("Clipped render", result["output"])
charge = report["renders"][0]
assert round(CHARGE_LOOP_START * 1000) <= charge["last_second_timeline_min"] <= charge["last_second_timeline_max"] <= round(CHARGE_LOOP_END * 1000)
# FMOD retains short internal ramp/tail channels around each transition;
# these Core counts include those channels, not just the two audible proxies.
assert 2 <= charge["last_second_real_channels_max"] <= 3, "Unexpected scan transition voice count"
assert charge["last_second_real_channels_min"] >= 1, "Scan loop must not leave a gap"
assert len(charge["wrap_periods_seconds"]) >= 8, "Too few scan loops to verify the period"
assert all(abs(period - CHARGE_LOOP_PERIOD) < 256 / 48000 + 1e-6 for period in charge["wrap_periods_seconds"])
assert charge["repeat_loop_is_best_local_match"], "Scan sustain period changed"
# Compare like windows: the full render includes the attack and stopped tail.
assert charge["repeat_loop_rms_error"] < charge["steady_rms"] * .1, "Scan sustain is unstable"
assert not charge["one_shot"] and report["renders"][1]["one_shot"] and not report["renders"][2]["one_shot"]
print(json.dumps(report, indent=2))
