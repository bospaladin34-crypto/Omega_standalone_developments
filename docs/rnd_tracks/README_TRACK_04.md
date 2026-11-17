# Track 04: Hardware-Free 19 kHz Golay Spatial Audio, MPE & Shader Studio

- **Reserved Loopback Port:** `127.0.0.1:19804`
- **Google Task Tracker:** [Track 04: Hardware-Free 19 kHz Golay Spatial Audio, MPE & Shader Studio](https://tasks.google.com/task/Kp2YcFemUYHQTmjD?sa=DLSA_GEMINI)
- **Mandate:** Strict Zero-Python, UTF-8 No-BOM, Sub-70MB Native Working-Set, Fail-Closed Telemetry.

## Core Engineering & Edge-Case Targets

- Turn stock laptop speakers/mics + phone OLED/IMU into a 0.42 mm 19 kHz Golay ultrasonic + S^3->S^2 Hopf studio.
- Guarantee zero-GC DSP thread safety via fixed 64 KB ring buffers and 180k frm/s lock-free shared memory.
- Bridge spatial gestures cleanly to DAWs and engines via low-jitter OSC, MPE MIDI, and CLAP/VST3.
